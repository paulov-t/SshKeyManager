namespace SshKeyManager.Services;

public sealed record AgentSnapshot(bool Running, IReadOnlyList<string> Keys, string? Error)
{
    public static AgentSnapshot NotRunning(string error) => new(false, Array.Empty<string>(), error);
}

/// <summary>Talks to the running ssh-agent through <c>ssh-add</c>.</summary>
public static class SshAgentService
{
    public static async Task<AgentSnapshot> SnapshotAsync(CancellationToken cancellationToken = default)
    {
        if (SshTools.SshAdd is null)
            return AgentSnapshot.NotRunning("ssh-add was not found. Install the OpenSSH client.");

        var result = await ProcessRunner.RunAsync(
            SshTools.SshAdd, new[] { "-L" }, timeoutMs: 10_000, cancellationToken: cancellationToken);

        if (result.ExitCode == 0)
        {
            var keys = result.StdOut
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(KeyText.Normalize)
                .Where(k => k.Length > 0)
                .ToList();
            return new AgentSnapshot(true, keys, null);
        }

        var message = result.Combined;
        if (result.ExitCode == 1 && message.Contains("no identities", StringComparison.OrdinalIgnoreCase))
            return new AgentSnapshot(true, Array.Empty<string>(), null);

        return AgentSnapshot.NotRunning(string.IsNullOrWhiteSpace(message) ? "ssh-agent is not reachable." : message);
    }

    /// <summary>Adds a private key. Passphrase (if any) is fed to ssh-add over stdin.</summary>
    public static async Task<ProcessResult> AddAsync(
        string privateKeyPath,
        string? passphrase,
        CancellationToken cancellationToken = default)
    {
        if (SshTools.SshAdd is null)
            return new ProcessResult(-1, string.Empty, "ssh-add was not found.", false);

        var stdin = passphrase is null ? null : passphrase + "\n";
        return await ProcessRunner.RunAsync(
            SshTools.SshAdd, new[] { privateKeyPath }, stdin, timeoutMs: 20_000, cancellationToken: cancellationToken);
    }

    public static async Task<ProcessResult> RemoveAsync(
        string privateKeyPath,
        CancellationToken cancellationToken = default)
    {
        if (SshTools.SshAdd is null)
            return new ProcessResult(-1, string.Empty, "ssh-add was not found.", false);

        return await ProcessRunner.RunAsync(
            SshTools.SshAdd, new[] { "-d", privateKeyPath }, timeoutMs: 15_000, cancellationToken: cancellationToken);
    }

    public static async Task<ProcessResult> TryStartServiceAsync(CancellationToken cancellationToken = default)
    {
        return await ProcessRunner.RunAsync(
            "powershell.exe",
            new[]
            {
                "-NoProfile",
                "-NonInteractive",
                "-Command",
                "Start-Service ssh-agent -ErrorAction Stop; (Get-Service ssh-agent).Status"
            },
            timeoutMs: 20_000,
            cancellationToken: cancellationToken);
    }

    public static string FriendlyError(ProcessResult result, bool encryptedKey)
    {
        var text = result.Combined;
        if (text.Contains("Could not open a connection", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("error connecting to agent", StringComparison.OrdinalIgnoreCase))
        {
            return "ssh-agent is not running. Start the 'OpenSSH Authentication Agent' service " +
                   "(services.msc) or use the \"Start agent\" button, then try again.";
        }

        if (text.Contains("incorrect passphrase", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("bad passphrase", StringComparison.OrdinalIgnoreCase))
        {
            return "The passphrase for this key is incorrect.";
        }

        if (result.TimedOut)
            return "ssh-add timed out while waiting for a passphrase.";

        if (encryptedKey && result.ExitCode != 0)
            return "Could not load the key. If it has a passphrase, make sure ssh-agent is running " +
                   "and enter the passphrase when prompted.\n\n" + text;

        return string.IsNullOrWhiteSpace(text) ? $"ssh-add exited with code {result.ExitCode}." : text;
    }
}
