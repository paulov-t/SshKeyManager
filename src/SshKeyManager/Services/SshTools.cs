using System.IO;

namespace SshKeyManager.Services;

/// <summary>Locates the OpenSSH tools installed on this machine.</summary>
public static class SshTools
{
    private const string KeygenOverrideVariable = "SSHKEYMANAGER_SSH_KEYGEN";
    private const string SshAddOverrideVariable = "SSHKEYMANAGER_SSH_ADD";
    private const string SshOverrideVariable = "SSHKEYMANAGER_SSH";

    public static string? Keygen { get; } = Find("ssh-keygen", KeygenOverrideVariable);
    public static string? SshAdd { get; } = Find("ssh-add", SshAddOverrideVariable);
    public static string? Ssh { get; } = Find("ssh", SshOverrideVariable);

    public static bool IsComplete => Keygen is not null && SshAdd is not null && Ssh is not null;

    public static string? MissingTools
    {
        get
        {
            var missing = new List<string>();
            if (Keygen is null) missing.Add("ssh-keygen");
            if (SshAdd is null) missing.Add("ssh-add");
            if (Ssh is null) missing.Add("ssh");
            return missing.Count == 0 ? null : string.Join(", ", missing);
        }
    }

    /// <summary>
    /// Resolves an OpenSSH executable the same way a shell would: an explicit override,
    /// then PATH, then the usual install locations for the current platform.
    /// </summary>
    private static string? Find(string baseName, string overrideVariable)
    {
        var overridden = Environment.GetEnvironmentVariable(overrideVariable);
        if (!string.IsNullOrWhiteSpace(overridden))
        {
            var expanded = Environment.ExpandEnvironmentVariables(overridden.Trim().Trim('"'));
            if (File.Exists(expanded))
                return expanded;
        }

        // Windows ships ssh-keygen.exe; Linux and macOS use the bare name.
        var fileName = OperatingSystem.IsWindows() ? baseName + ".exe" : baseName;

        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = TryCombine(directory.Trim('"'), fileName);
            if (candidate is not null && File.Exists(candidate))
                return candidate;
        }

        foreach (var directory in FallbackDirectories())
        {
            var candidate = TryCombine(directory, fileName);
            if (candidate is not null && File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    private static string? TryCombine(string directory, string fileName)
    {
        if (string.IsNullOrWhiteSpace(directory))
            return null;

        try
        {
            return Path.Combine(directory, fileName);
        }
        catch
        {
            // Malformed PATH entry.
            return null;
        }
    }

    private static IEnumerable<string> FallbackDirectories()
    {
        if (!OperatingSystem.IsWindows())
        {
            // Standard locations on Linux, macOS (Intel and Apple silicon) and BSD.
            yield return "/usr/bin";
            yield return "/usr/local/bin";
            yield return "/bin";
            yield return "/opt/homebrew/bin";
            yield return "/opt/local/bin";
            yield break;
        }

        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (!string.IsNullOrEmpty(windows))
            yield return Path.Combine(windows, "System32", "OpenSSH");

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (!string.IsNullOrEmpty(programFiles))
        {
            yield return Path.Combine(programFiles, "OpenSSH");
            // Git for Windows also packages ssh-keygen and friends.
            yield return Path.Combine(programFiles, "Git", "usr", "bin");
            yield return Path.Combine(programFiles, "Git", "bin");
        }

        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (!string.IsNullOrEmpty(programFilesX86))
            yield return Path.Combine(programFilesX86, "Git", "usr", "bin");
    }

    /// <summary>Human-readable install guidance used in error messages.</summary>
    public static string InstallHint =>
        "Install the OpenSSH client and make sure ssh-keygen is on PATH. " +
        "Windows: Settings → System → Optional features → OpenSSH Client. " +
        "Debian/Ubuntu: sudo apt-get install -y openssh-client. " +
        "macOS: preinstalled (/usr/bin/ssh-keygen). " +
        "You can also point the app at a specific binary with " +
        "SSHKEYMANAGER_SSH_KEYGEN, SSHKEYMANAGER_SSH_ADD and SSHKEYMANAGER_SSH.";
}
