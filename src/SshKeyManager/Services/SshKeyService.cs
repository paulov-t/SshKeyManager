using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using SshKeyManager.Models;

namespace SshKeyManager.Services;

/// <summary>Reads, creates, exports, backs up and deletes keys inside the user's ~/.ssh folder.</summary>
public sealed partial class SshKeyService
{
    private static readonly HashSet<string> IgnoredNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "config", "known_hosts", "known_hosts.old", "authorized_keys", "authorized_keys2",
        "environment", ".ds_store"
    };

    [GeneratedRegex(@"^\s*(?<bits>\d+)\s+(?<fp>\S+)\s*(?<comment>.*?)\s*\((?<type>[A-Za-z0-9_\-]+)\)\s*$",
        RegexOptions.Compiled)]
    private static partial Regex FingerprintRegex();

    public SshKeyService(string? sshDirectory = null)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(home))
            home = Environment.GetEnvironmentVariable("USERPROFILE") ?? Directory.GetCurrentDirectory();

        SshDirectory = sshDirectory ?? Path.Combine(home, ".ssh");
        BackupDirectory = Path.Combine(home, ".ssh-backups");
    }

    public string SshDirectory { get; }
    public string BackupDirectory { get; }

    /// <summary>Non-fatal problem from the last generation run (for example, ssh-agent unavailable).</summary>
    public string? LastGenerationWarning { get; private set; }

    public void EnsureDirectory() => Directory.CreateDirectory(SshDirectory);

    public string DefaultBackupPath() =>
        Path.Combine(BackupDirectory, $"ssh-backup-{DateTime.Now:yyyyMMdd-HHmmss}.zip");

    public string SuggestKeyName(string algorithm)
    {
        var baseName = algorithm switch
        {
            "RSA" => "id_rsa",
            "ECDSA" => "id_ecdsa",
            _ => "id_ed25519"
        };

        if (!File.Exists(Path.Combine(SshDirectory, baseName)) &&
            !File.Exists(Path.Combine(SshDirectory, baseName + ".pub")))
            return baseName;

        for (var i = 2; i < 100; i++)
        {
            var candidate = $"{baseName}_{i}";
            if (!File.Exists(Path.Combine(SshDirectory, candidate)) &&
                !File.Exists(Path.Combine(SshDirectory, candidate + ".pub")))
                return candidate;
        }

        return baseName + "_" + DateTime.Now.Ticks;
    }

    public async Task<IReadOnlyList<SshKeyInfo>> LoadKeysAsync(
        IReadOnlySet<string> agentKeys,
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(SshDirectory))
            return Array.Empty<SshKeyInfo>();

        var results = new List<SshKeyInfo>();
        var candidates = Directory.EnumerateFiles(SshDirectory)
            .Where(LooksLikeCandidate)
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var path in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var info = await InspectAsync(path, cancellationToken).ConfigureAwait(false);
            if (info is null)
                continue;

            info.IsInAgent = agentKeys.Contains(KeyText.Normalize(info.PublicKeyText));
            results.Add(info);
        }

        return results;
    }

    public async Task<SshKeyInfo?> InspectAsync(string path, CancellationToken cancellationToken = default)
    {
        var keygen = SshTools.Keygen;
        if (keygen is null)
            return null;

        var fileName = Path.GetFileName(path);
        var isPublicFile = fileName.EndsWith(".pub", StringComparison.OrdinalIgnoreCase);

        // An orphan .pub is listed; a .pub next to its private key is represented by the private key.
        if (isPublicFile && File.Exists(path[..^4]))
            return null;

        var fingerprint = await ProcessRunner.RunAsync(
            keygen, new[] { "-l", "-E", "sha256", "-f", path }, timeoutMs: 15_000, cancellationToken: cancellationToken);

        if (fingerprint.ExitCode != 0)
            return null;

        var match = FingerprintRegex().Match(fingerprint.StdOut.Trim());
        if (!match.Success)
            return null;

        var bits = int.TryParse(match.Groups["bits"].Value, out var parsedBits) ? parsedBits : 0;
        var type = match.Groups["type"].Value.ToUpperInvariant();
        var algorithm = type switch
        {
            "ED25519" => "Ed25519",
            "RSA" => "RSA",
            "ECDSA" => "ECDSA",
            "DSA" => "DSA",
            _ => type
        };

        var publicPath = isPublicFile ? path : path + ".pub";
        var hasPublic = File.Exists(publicPath);
        var publicText = string.Empty;

        if (hasPublic)
        {
            try { publicText = (await File.ReadAllTextAsync(publicPath, cancellationToken)).Trim(); }
            catch { publicText = string.Empty; }
        }

        var encrypted = false;
        if (!isPublicFile)
        {
            var derive = await ProcessRunner.RunAsync(
                keygen, new[] { "-y", "-P", "", "-f", path }, timeoutMs: 15_000, cancellationToken: cancellationToken);

            if (derive.ExitCode == 0)
            {
                if (publicText.Length == 0)
                    publicText = derive.StdOut.Trim();
            }
            else if (derive.Combined.Contains("passphrase", StringComparison.OrdinalIgnoreCase))
            {
                encrypted = true;
            }
        }

        var md5 = await ProcessRunner.RunAsync(
            keygen, new[] { "-l", "-E", "md5", "-f", path }, timeoutMs: 15_000, cancellationToken: cancellationToken);
        var md5Match = FingerprintRegex().Match(md5.StdOut.Trim());

        var comment = match.Groups["comment"].Value.Trim();
        if (comment.Length == 0)
            comment = KeyText.Comment(publicText);

        var file = new FileInfo(path);

        return new SshKeyInfo
        {
            Name = fileName,
            PrivatePath = path,
            PublicPath = hasPublic ? publicPath : null,
            HasPublicKey = hasPublic,
            IsPublicOnly = isPublicFile,
            Algorithm = algorithm,
            Bits = bits,
            Comment = comment,
            Sha256Fingerprint = match.Groups["fp"].Value,
            Md5Fingerprint = md5Match.Success ? md5Match.Groups["fp"].Value : string.Empty,
            PublicKeyText = publicText,
            IsEncrypted = encrypted,
            SizeBytes = file.Exists ? file.Length : 0,
            ModifiedUtc = file.Exists ? file.LastWriteTimeUtc : DateTime.UtcNow
        };
    }

    public async Task<SshKeyInfo> GenerateAsync(
        KeyGenerationRequest request,
        bool overwrite,
        CancellationToken cancellationToken = default)
    {
        if (SshTools.Keygen is null)
            throw new InvalidOperationException("ssh-keygen was not found. " + SshTools.InstallHint);

        LastGenerationWarning = null;

        var name = request.Name.Trim();
        if (name.Length == 0)
            throw new InvalidOperationException("The key name cannot be empty.");

        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Contains('/') || name.Contains('\\'))
            throw new InvalidOperationException("The key name contains characters that are not allowed in a file name.");

        EnsureDirectory();
        var privatePath = Path.Combine(SshDirectory, name);
        var publicPath = privatePath + ".pub";

        if (!overwrite && (File.Exists(privatePath) || File.Exists(publicPath)))
            throw new InvalidOperationException($"'{name}' already exists in {SshDirectory}.");

        if (overwrite)
        {
            TryDelete(privatePath);
            TryDelete(publicPath);
        }

        var arguments = new List<string> { "-q", "-t", MapAlgorithm(request.Algorithm) };

        if (request.Algorithm != KeyAlgorithm.Ed25519)
        {
            arguments.Add("-b");
            arguments.Add(request.Bits.ToString());
        }

        arguments.Add("-C");
        arguments.Add(request.Comment);
        arguments.Add("-N");
        arguments.Add(request.Passphrase ?? string.Empty);
        arguments.Add("-f");
        arguments.Add(privatePath);

        var result = await ProcessRunner.RunAsync(
            SshTools.Keygen, arguments, timeoutMs: 60_000, cancellationToken: cancellationToken);

        if (result.ExitCode != 0)
            throw new InvalidOperationException(
                $"ssh-keygen failed: {(string.IsNullOrWhiteSpace(result.Combined) ? $"exit code {result.ExitCode}" : result.Combined)}");

        var info = await InspectAsync(privatePath, cancellationToken)
                   ?? throw new InvalidOperationException("The key was created but could not be read back.");

        if (request.AddToAgent)
        {
            var add = await SshAgentService.AddAsync(privatePath, request.Passphrase, cancellationToken);
            if (add.ExitCode != 0)
            {
                LastGenerationWarning =
                    "The key was created, but it could not be added to ssh-agent.\n\n" +
                    SshAgentService.FriendlyError(add, info.IsEncrypted);
            }
        }

        return info;
    }

    public async Task DeleteAsync(
        SshKeyInfo key,
        bool deletePublicKey,
        CancellationToken cancellationToken = default)
    {
        if (!IsInsideSshDirectory(key.PrivatePath))
            throw new InvalidOperationException("Refusing to delete a file outside of ~/.ssh.");

        if (key.IsInAgent)
        {
            try { await SshAgentService.RemoveAsync(key.PrivatePath, cancellationToken); }
            catch { /* best effort */ }
        }

        TryDelete(key.PrivatePath);

        if (deletePublicKey && !key.IsPublicOnly && key.PublicPath is not null && IsInsideSshDirectory(key.PublicPath))
            TryDelete(key.PublicPath);
    }

    public async Task ExportPublicKeyAsync(
        SshKeyInfo key,
        string destination,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key.PublicKeyText))
            throw new InvalidOperationException("This key has no public key text to export.");

        var directory = Path.GetDirectoryName(destination);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        await File.WriteAllTextAsync(
            destination,
            key.PublicKeyText.Trim() + Environment.NewLine,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            cancellationToken);
    }

    public async Task<string> CreateBackupAsync(string destinationPath, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(SshDirectory))
            throw new DirectoryNotFoundException($"The folder {SshDirectory} does not exist.");

        var directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        TryDelete(destinationPath);

        await Task.Run(() =>
        {
            using var zip = ZipFile.Open(destinationPath, ZipArchiveMode.Create);
            foreach (var file in Directory.EnumerateFiles(SshDirectory))
            {
                try
                {
                    zip.CreateEntryFromFile(file, Path.GetFileName(file), CompressionLevel.Optimal);
                }
                catch
                {
                    // Locked or transient files are skipped.
                }
            }
        }, cancellationToken);

        return destinationPath;
    }

    public bool IsInsideSshDirectory(string path)
    {
        var root = Path.GetFullPath(SshDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(path);
        return full.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    private static string MapAlgorithm(KeyAlgorithm algorithm) => algorithm switch
    {
        KeyAlgorithm.Rsa => "rsa",
        KeyAlgorithm.Ecdsa => "ecdsa",
        _ => "ed25519"
    };

    private static bool LooksLikeCandidate(string path)
    {
        var name = Path.GetFileName(path);
        if (name.Length == 0 || name.StartsWith('.'))
            return false;

        if (IgnoredNames.Contains(name))
            return false;

        var lower = name.ToLowerInvariant();

        if (lower.StartsWith("config.") || lower.StartsWith("known_hosts") ||
            lower.StartsWith("authorized_keys") || lower.StartsWith("agent."))
            return false;

        if (lower.EndsWith(".old") || lower.EndsWith(".bak") || lower.EndsWith(".tmp") ||
            lower.EndsWith(".log") || lower.EndsWith(".zip") || lower.EndsWith(".sock") ||
            lower.EndsWith(".lock") || lower.EndsWith("-cert.pub"))
            return false;

        return true;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Callers re-check existence where it matters.
        }
    }
}
