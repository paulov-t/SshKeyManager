using System.IO;
using SshKeyManager.Services;

namespace SshKeyManager.Tests;

public sealed class SshToolsTests
{
    [Fact]
    public void Finds_the_openssh_tools_on_this_machine()
    {
        Assert.True(SshTools.Keygen is not null,
            "ssh-keygen was not found. " + SshTools.InstallHint);
        Assert.True(SshTools.SshAdd is not null,
            "ssh-add was not found. " + SshTools.InstallHint);
        Assert.True(SshTools.Ssh is not null,
            "ssh was not found. " + SshTools.InstallHint);

        Assert.True(File.Exists(SshTools.Keygen!));
        Assert.True(File.Exists(SshTools.SshAdd!));
        Assert.True(File.Exists(SshTools.Ssh!));
    }

    [Fact]
    public void Resolves_platform_specific_file_names()
    {
        // On Linux/macOS the binaries have no extension; on Windows they are .exe files.
        var expectedName = OperatingSystem.IsWindows() ? "ssh-keygen.exe" : "ssh-keygen";

        Assert.Equal(expectedName, Path.GetFileName(SshTools.Keygen));
    }
}
