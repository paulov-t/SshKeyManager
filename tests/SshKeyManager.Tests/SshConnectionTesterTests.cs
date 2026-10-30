using SshKeyManager.Services;

namespace SshKeyManager.Tests;

public sealed class SshConnectionTesterTests
{
    [Fact]
    public void Azure_devops_is_offered_as_a_preset()
    {
        Assert.Contains(("Azure DevOps", "git@ssh.dev.azure.com"), SshConnectionTester.Presets);
    }

    [Fact]
    public void Azure_devops_shell_rejection_counts_as_success()
    {
        const string output =
            "Warning: Permanently added 'ssh.dev.azure.com' (RSA) to the list of known hosts.\r\n" +
            "remote: Shell access is not supported.\r\n" +
            "shell request failed on channel 0\r\n";

        var result = SshConnectionTester.Interpret(
            "git@ssh.dev.azure.com", "/home/me/.ssh/id_ed25519", output, exitCode: 255, timedOut: false);

        Assert.True(result.Success);
        Assert.Equal("Key accepted by Azure DevOps", result.Summary);
        Assert.Contains("does not offer shell access", result.Details);
    }

    [Fact]
    public void Azure_devops_rejected_key_is_a_failure()
    {
        const string output =
            "remote: Public key authentication failed.\r\n" +
            "fatal: Could not read from remote repository.\r\n";

        var result = SshConnectionTester.Interpret(
            "git@ssh.dev.azure.com", "/home/me/.ssh/id_ed25519", output, exitCode: 255, timedOut: false);

        Assert.False(result.Success);
        Assert.Contains("rejected this key", result.Details);
    }

    [Fact]
    public void Azure_devops_expired_key_gets_a_specific_hint()
    {
        const string output =
            "remote: Authentication failed: your SSH key has expired.\r\n" +
            "remote: Public key authentication failed.\r\n";

        var result = SshConnectionTester.Interpret(
            "git@ssh.dev.azure.com", "/home/me/.ssh/id_ed25519", output, exitCode: 255, timedOut: false);

        Assert.False(result.Success);
        Assert.Contains("expired", result.Details);
    }

    [Fact]
    public void Generic_permission_denied_with_extra_methods_is_recognised()
    {
        var result = SshConnectionTester.Interpret(
            "git@example.com", "/home/me/.ssh/id_ed25519",
            "git@example.com: Permission denied (password,publickey).", exitCode: 255, timedOut: false);

        Assert.False(result.Success);
        Assert.Contains("rejected this key", result.Details);
    }

    [Fact]
    public void GitHub_greeting_counts_as_success_despite_exit_code_one()
    {
        var result = SshConnectionTester.Interpret(
            "git@github.com", "/home/me/.ssh/id_ed25519",
            "Hi octocat! You've successfully authenticated, but GitHub does not provide shell access.",
            exitCode: 1, timedOut: false);

        Assert.True(result.Success);
        Assert.Contains("successfully authenticated", result.Summary);
    }

    [Fact]
    public void Shell_rejection_from_an_unknown_host_is_not_success()
    {
        var result = SshConnectionTester.Interpret(
            "git@example.com", "/home/me/.ssh/id_ed25519",
            "remote: Shell access is not supported.", exitCode: 255, timedOut: false);

        Assert.False(result.Success);
    }

    [Fact]
    public void Timeout_is_reported()
    {
        var result = SshConnectionTester.Interpret(
            "git@github.com", "/home/me/.ssh/id_ed25519", string.Empty, exitCode: -1, timedOut: true);

        Assert.False(result.Success);
        Assert.Contains("timed out", result.Details);
    }
}
