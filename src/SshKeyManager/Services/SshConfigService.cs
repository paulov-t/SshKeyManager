using System.IO;
using System.Text;
using SshKeyManager.Models;

namespace SshKeyManager.Services;

public sealed class SshConfigDocument
{
    /// <summary>Lines before the first Host block (global directives, includes, comments).</summary>
    public List<string> GlobalLines { get; } = new();

    public List<SshHostEntry> Hosts { get; } = new();

    public string NewLine { get; set; } = "\n";

    /// <summary>Set when the file contains Match blocks, which this editor will not rewrite.</summary>
    public bool HasUnsupportedBlocks { get; set; }

    public string RawText { get; set; } = string.Empty;
}

/// <summary>Parses and writes ~/.ssh/config while preserving unknown directives.</summary>
public sealed class SshConfigService
{
    public SshConfigService(string? sshDirectory = null)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(home))
            home = Environment.GetEnvironmentVariable("USERPROFILE") ?? Directory.GetCurrentDirectory();

        var directory = sshDirectory ?? Path.Combine(home, ".ssh");
        ConfigPath = Path.Combine(directory, "config");
    }

    public string ConfigPath { get; }

    public SshConfigDocument Load()
    {
        var document = new SshConfigDocument();

        if (!File.Exists(ConfigPath))
            return document;

        var text = File.ReadAllText(ConfigPath);
        document.RawText = text;
        document.NewLine = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

        // Match blocks hold conditional settings that cannot be represented per Host.
        if (lines.Any(l => l.TrimStart().StartsWith("Match ", StringComparison.OrdinalIgnoreCase)))
        {
            document.HasUnsupportedBlocks = true;
            return document;
        }

        SshHostEntry? current = null;

        foreach (var raw in lines)
        {
            var trimmed = raw.Trim();

            if (trimmed.Length == 0)
                continue;

            if (TryParseHostLine(trimmed, out var aliases))
            {
                current = new SshHostEntry { Alias = aliases };
                document.Hosts.Add(current);
                continue;
            }

            if (current is null)
            {
                document.GlobalLines.Add(raw);
                continue;
            }

            if (trimmed.StartsWith('#'))
            {
                AppendExtra(current, raw);
                continue;
            }

            var (keyword, value) = SplitDirective(trimmed);

            switch (keyword.ToLowerInvariant())
            {
                case "hostname":
                    current.HostName = value;
                    break;
                case "user":
                    current.User = value;
                    break;
                case "port":
                    current.Port = value;
                    break;
                case "identityfile":
                    if (string.IsNullOrWhiteSpace(current.IdentityFile))
                        current.IdentityFile = value;
                    else
                        AppendExtra(current, raw);
                    break;
                case "identitiesonly":
                    current.IdentitiesOnly = value.Equals("yes", StringComparison.OrdinalIgnoreCase) || value == "true";
                    break;
                default:
                    AppendExtra(current, raw);
                    break;
            }
        }

        return document;
    }

    /// <summary>Writes the structured host list. Returns the backup file created, if any.</summary>
    public async Task<string?> SaveAsync(
        SshConfigDocument document,
        IReadOnlyList<SshHostEntry> hosts,
        CancellationToken cancellationToken = default)
    {
        var newLine = document.NewLine;
        var builder = new StringBuilder();

        foreach (var line in document.GlobalLines)
            builder.Append(line).Append(newLine);

        if (document.GlobalLines.Count > 0 && hosts.Count > 0)
            builder.Append(newLine);

        for (var index = 0; index < hosts.Count; index++)
        {
            var host = hosts[index];
            if (string.IsNullOrWhiteSpace(host.Alias))
                continue;

            builder.Append("Host ").Append(host.Alias.Trim()).Append(newLine);
            AppendDirective(builder, newLine, "HostName", host.HostName);
            AppendDirective(builder, newLine, "User", host.User);
            AppendDirective(builder, newLine, "Port", host.Port);
            AppendDirective(builder, newLine, "IdentityFile", host.IdentityFile);

            if (host.IdentitiesOnly)
                builder.Append("    IdentitiesOnly yes").Append(newLine);

            if (!string.IsNullOrWhiteSpace(host.ExtraOptions))
            {
                var extraLines = host.ExtraOptions
                    .Replace("\r\n", "\n").Replace('\r', '\n')
                    .Split('\n');

                foreach (var line in extraLines)
                {
                    if (line.Trim().Length == 0)
                        continue;

                    if (line.StartsWith(' ') || line.StartsWith('\t'))
                        builder.Append(line.TrimEnd()).Append(newLine);
                    else
                        builder.Append("    ").Append(line.TrimEnd()).Append(newLine);
                }
            }

            if (index < hosts.Count - 1)
                builder.Append(newLine);
        }

        return await WriteRawAsync(builder.ToString(), cancellationToken);
    }

    /// <summary>Replaces the config with arbitrary text (used by the raw editor).</summary>
    public async Task<string?> WriteRawAsync(string content, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(ConfigPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        string? backup = null;
        if (File.Exists(ConfigPath))
        {
            backup = Path.Combine(
                Path.GetDirectoryName(ConfigPath)!,
                $"config.bak-{DateTime.Now:yyyyMMdd-HHmmss-fff}");
            File.Copy(ConfigPath, backup, overwrite: true);
        }

        if (!content.EndsWith('\n'))
            content += Environment.NewLine;

        var temp = ConfigPath + ".tmp";
        await File.WriteAllTextAsync(
            temp, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken);
        File.Move(temp, ConfigPath, overwrite: true);

        return backup;
    }

    public static void AppendExtra(SshHostEntry host, string rawLine)
    {
        host.ExtraOptions = string.IsNullOrEmpty(host.ExtraOptions)
            ? rawLine
            : host.ExtraOptions + Environment.NewLine + rawLine;
    }

    private static void AppendDirective(StringBuilder builder, string newLine, string keyword, string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            builder.Append("    ").Append(keyword).Append(' ').Append(value.Trim()).Append(newLine);
    }

    private static bool TryParseHostLine(string trimmed, out string aliases)
    {
        aliases = string.Empty;

        if (!trimmed.StartsWith("Host", StringComparison.OrdinalIgnoreCase))
            return false;

        if (trimmed.Length <= 4)
            return false;

        var rest = trimmed[4..];
        if (rest[0] != ' ' && rest[0] != '\t')
            return false;

        var value = rest.Trim();
        if (value.Length == 0)
            return false;

        aliases = value;
        return true;
    }

    private static (string Keyword, string Value) SplitDirective(string trimmed)
    {
        var separator = trimmed.IndexOfAny(new[] { ' ', '\t', '=' });
        if (separator < 0)
            return (trimmed, string.Empty);

        var keyword = trimmed[..separator].Trim();
        var value = trimmed[(separator + 1)..].Trim().TrimStart('=').Trim();
        return (keyword, value);
    }
}
