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
        ("Azure DevOps", "git@ssh.dev.azure.com"),
        ("Codeberg", "git@codeberg.org")
    };

    public static async Task<ConnectionTestResult> TestAsync(
        string keyPath,
        string target,
        CancellationToken cancellationToken = default)
    {
        if (SshTools.Ssh is null)
            return new ConnectionTestResult(false, "OpenSSH not found",
                "ssh was not found. " + SshTools.InstallHint, string.Empty);

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

        return Interpret(target, keyPath, result.Combined, result.ExitCode, result.TimedOut);
    }

    /// <summary>Classifies the output of an <c>ssh -T</c> attempt. Public so it can be unit tested.</summary>
    public static ConnectionTestResult Interpret(
        string target,
        string keyPath,
        string output,
        int exitCode,
        bool timedOut)
    {
        var isAzureDevOps =
            target.Contains("ssh.dev.azure.com", StringComparison.OrdinalIgnoreCase) ||
            target.Contains("vs-ssh.visualstudio.com", StringComparison.OrdinalIgnoreCase);

        // Azure DevOps has no interactive shell: after a successful login it answers with
        // "remote: Shell access is not supported." and a non-zero exit code.
        var shellBlocked = output.Contains("Shell access is not supported", StringComparison.OrdinalIgnoreCase);

        var success = exitCode == 0 ||
                      output.Contains("successfully authenticated", StringComparison.OrdinalIgnoreCase) ||
                      output.Contains("Welcome to GitLab", StringComparison.OrdinalIgnoreCase) ||
                      (isAzureDevOps && shellBlocked);

        var summary = isAzureDevOps && shellBlocked
            ? "Key accepted by Azure DevOps"
            : FirstMeaningfulLine(output);

        if (success && summary.Length == 0)
            summary = "Authenticated successfully.";

        var details = success
            ? isAzureDevOps && shellBlocked
                ? "Azure DevOps accepted the key. The service does not offer shell access, so " +
                  "\"Shell access is not supported\" is the expected response and means authentication worked."
                : $"The server accepted the key {Path.GetFileName(keyPath)}."
            : BuildFailureHint(output, timedOut);

        return new ConnectionTestResult(success, summary, details, output);
    }

    private static string BuildFailureHint(string output, bool timedOut)
    {
        if (timedOut)
            return "The connection timed out. Check your network or the target host.";

        if (output.Contains("your SSH key has expired", StringComparison.OrdinalIgnoreCase))
            return "The service reports that this SSH key has expired. Upload a new public key to your profile.";

        if (output.Contains("Public key authentication failed", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("Permission denied", StringComparison.OrdinalIgnoreCase))
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
