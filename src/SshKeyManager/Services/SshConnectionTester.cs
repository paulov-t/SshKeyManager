using System.Text.RegularExpressions;

namespace SshKeyManager.Services;

public sealed record ConnectionTestResult(bool Success, string Summary, string Details, string RawOutput);

public static partial class SshConnectionTester
{
    public static readonly (string Label, string Target)[] Presets =
    {
        ("GitHub", "git@github.com"),
        ("GitLab", "git@gitlab.com"),
        ("Bitbucket", "git@bitbucket.org"),
        ("Codeberg", "git@codeberg.org")
    };

    public static async Task<ConnectionTestResult> TestAsync(
        string keyPath,
        string target,
        CancellationToken cancellationToken = default)
    {
        if (SshTools.Ssh is null)
            return new ConnectionTestResult(false, "OpenSSH not found",
                "ssh.exe was not found on this machine. Install the OpenSSH client to test connections.", string.Empty);

        if (string.IsNullOrWhiteSpace(target))
            return new ConnectionTestResult(false, "No target", "Enter a target such as git@github.com.", string.Empty);

        var arguments = new List<string>
        {
            "-T",
            "-i", keyPath,
            "-o", "IdentitiesOnly=yes",
            "-o", "StrictHostKeyChecking=accept-new",
            "-o", "BatchMode=yes",
            "-o", "ConnectTimeout=10",
            target.Trim()
        };

        var result = await ProcessRunner.RunAsync(
            SshTools.Ssh, arguments, timeoutMs: 30_000, cancellationToken: cancellationToken);

        var output = result.Combined;
        var success = result.ExitCode == 0 ||
                      output.Contains("successfully authenticated", StringComparison.OrdinalIgnoreCase) ||
                      output.Contains("Welcome to GitLab", StringComparison.OrdinalIgnoreCase);

        var summary = FirstMeaningfulLine(output);
        if (success && summary.Length == 0)
            summary = "Authenticated successfully.";

        var details = success
            ? $"The server accepted the key {Path.GetFileName(keyPath)}."
            : BuildFailureHint(output, result.TimedOut);

        return new ConnectionTestResult(success, summary, details, output);
    }

    private static string BuildFailureHint(string output, bool timedOut)
    {
        if (timedOut)
            return "The connection timed out. Check your network or the target host.";

        if (output.Contains("Permission denied (publickey)", StringComparison.OrdinalIgnoreCase))
            return "The server rejected this key. Add the public key to your account on the target service, " +
                   "or add the key to ssh-agent if it is passphrase protected.";

        if (output.Contains("Could not resolve hostname", StringComparison.OrdinalIgnoreCase))
            return "The host name could not be resolved.";

        if (output.Contains("load key", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("passphrase", StringComparison.OrdinalIgnoreCase))
            return "The key could not be loaded. If it is passphrase protected, add it to ssh-agent first.";

        return output.Length == 0 ? "ssh exited without any output." : "The connection test failed.";
    }

    private static string FirstMeaningfulLine(string output)
    {
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            if (trimmed.Length > 0 && !trimmed.StartsWith("Warning:", StringComparison.OrdinalIgnoreCase))
                return trimmed;
        }

        return string.Empty;
    }
}
