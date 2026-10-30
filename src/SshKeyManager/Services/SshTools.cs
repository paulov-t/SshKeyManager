using System.IO;

namespace SshKeyManager.Services;

/// <summary>Locates the OpenSSH tools installed on this machine.</summary>
public static class SshTools
{
    public static string? Keygen { get; } = Find("ssh-keygen.exe");
    public static string? SshAdd { get; } = Find("ssh-add.exe");
    public static string? Ssh { get; } = Find("ssh.exe");

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

    private static string? Find(string exeName)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(directory.Trim('"'), exeName);
                if (File.Exists(candidate))
                    return candidate;
            }
            catch
            {
                // Malformed PATH entry.
            }
        }

        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (!string.IsNullOrEmpty(windows))
        {
            var candidate = Path.Combine(windows, "System32", "OpenSSH", exeName);
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }
}
