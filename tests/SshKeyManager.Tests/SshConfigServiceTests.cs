using System.IO;
using SshKeyManager.Services;

namespace SshKeyManager.Tests;

public sealed class SshConfigServiceTests : IDisposable
{
    private readonly string _directory;
    private readonly SshConfigService _service;

    public SshConfigServiceTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "sshkm-config-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _service = new SshConfigService(_directory);
    }

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); }
        catch { /* best effort */ }
    }

    private void WriteConfig(string text) => File.WriteAllText(_service.ConfigPath, text);

    [Fact]
    public void Parses_hosts_and_global_lines()
    {
        WriteConfig(
            "# global comment\n" +
            "Include ~/.ssh/extra\n" +
            "\n" +
            "Host github.com github\n" +
            "    HostName github.com\n" +
            "    User git\n" +
            "    IdentityFile ~/.ssh/id_ed25519\n" +
            "\n" +
            "Host work\n" +
            "    HostName work.example.com\n" +
            "    User deploy\n" +
            "    Port 2222\n" +
            "    ServerAliveInterval 60\n");

        var document = _service.Load();

        Assert.False(document.HasUnsupportedBlocks);
        Assert.Equal(2, document.GlobalLines.Count);
        Assert.Equal(2, document.Hosts.Count);

        var github = document.Hosts[0];
        Assert.Equal("github.com github", github.Alias);
        Assert.Equal("github.com", github.HostName);
        Assert.Equal("git", github.User);
        Assert.Equal("~/.ssh/id_ed25519", github.IdentityFile);

        var work = document.Hosts[1];
        Assert.Equal("2222", work.Port);
        Assert.Contains("ServerAliveInterval 60", work.ExtraOptions);
    }

    [Fact]
    public async Task Save_preserves_unknown_directives_and_global_lines()
    {
        WriteConfig(
            "# global comment\n" +
            "Include ~/.ssh/extra\n" +
            "\n" +
            "Host work\n" +
            "    HostName work.example.com\n" +
            "    User deploy\n" +
            "    Port 2222\n" +
            "    ServerAliveInterval 60\n" +
            "    # keep this note\n");

        var document = _service.Load();
        document.Hosts[0].User = "someone";

        var backup = await _service.SaveAsync(document, document.Hosts);

        Assert.NotNull(backup);
        Assert.True(File.Exists(backup));

        var saved = File.ReadAllText(_service.ConfigPath);
        Assert.Contains("# global comment", saved);
        Assert.Contains("Include ~/.ssh/extra", saved);
        Assert.Contains("Host work", saved);
        Assert.Contains("User someone", saved);
        Assert.Contains("Port 2222", saved);
        Assert.Contains("ServerAliveInterval 60", saved);
        Assert.Contains("# keep this note", saved);
    }

    [Fact]
    public void Match_blocks_disable_structured_editing()
    {
        WriteConfig(
            "Host github.com\n" +
            "    User git\n" +
            "\n" +
            "Match host *.example.com\n" +
            "    User deploy\n");

        var document = _service.Load();

        Assert.True(document.HasUnsupportedBlocks);
        Assert.Empty(document.Hosts);
    }

    [Fact]
    public async Task WriteRaw_replaces_file_and_creates_backup()
    {
        WriteConfig("Host old\n    User git\n");

        var backup = await _service.WriteRawAsync("Host new\n    User deploy\n");

        Assert.NotNull(backup);
        Assert.Equal("Host new\n    User deploy\n", File.ReadAllText(_service.ConfigPath).Replace("\r\n", "\n"));
        Assert.Contains("Host old", File.ReadAllText(backup!));
    }
}
