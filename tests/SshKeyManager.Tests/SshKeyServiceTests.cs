using System.IO;
using SshKeyManager.Models;
using SshKeyManager.Services;

namespace SshKeyManager.Tests;

public sealed class SshKeyServiceTests : IDisposable
{
    private readonly string _directory;
    private readonly SshKeyService _service;

    public SshKeyServiceTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "sshkm-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _service = new SshKeyService(_directory);
    }

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); }
        catch { /* best effort */ }
    }

    [Fact]
    public async Task Generates_ed25519_key_without_passphrase()
    {
        var key = await _service.GenerateAsync(
            new KeyGenerationRequest("id_test", KeyAlgorithm.Ed25519, 256, "unit@test", null, false),
            overwrite: false);

        Assert.Equal("id_test", key.Name);
        Assert.Equal("Ed25519", key.Algorithm);
        Assert.Equal(256, key.Bits);
        Assert.Equal("unit@test", key.Comment);
        Assert.False(key.IsEncrypted);
        Assert.True(File.Exists(Path.Combine(_directory, "id_test")));
        Assert.True(File.Exists(Path.Combine(_directory, "id_test.pub")));
        Assert.StartsWith("ssh-ed25519 ", key.PublicKeyText);
        Assert.StartsWith("SHA256:", key.Sha256Fingerprint);
    }

    [Fact]
    public async Task Generates_rsa_key_with_passphrase()
    {
        var key = await _service.GenerateAsync(
            new KeyGenerationRequest("id_rsa_test", KeyAlgorithm.Rsa, 2048, "unit@test", "correct horse", false),
            overwrite: false);

        Assert.Equal("RSA", key.Algorithm);
        Assert.Equal(2048, key.Bits);
        Assert.True(key.IsEncrypted);
        Assert.StartsWith("ssh-rsa ", key.PublicKeyText);
    }

    [Fact]
    public async Task Generate_refuses_to_overwrite_without_permission()
    {
        await _service.GenerateAsync(
            new KeyGenerationRequest("id_dup", KeyAlgorithm.Ed25519, 256, "one", null, false),
            overwrite: false);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.GenerateAsync(
                new KeyGenerationRequest("id_dup", KeyAlgorithm.Ed25519, 256, "two", null, false),
                overwrite: false));
    }

    [Fact]
    public async Task Load_lists_keys_and_ignores_unrelated_files()
    {
        File.WriteAllText(Path.Combine(_directory, "known_hosts"), "example.com ssh-ed25519 AAAA");
        File.WriteAllText(Path.Combine(_directory, "config"), "Host example\n    User git\n");

        await _service.GenerateAsync(
            new KeyGenerationRequest("id_one", KeyAlgorithm.Ed25519, 256, "one@test", null, false), false);
        await _service.GenerateAsync(
            new KeyGenerationRequest("id_two", KeyAlgorithm.Ed25519, 256, "two@test", null, false), false);

        var keys = await _service.LoadKeysAsync(new HashSet<string>(StringComparer.Ordinal));

        Assert.Equal(2, keys.Count);
        Assert.All(keys, key => Assert.False(key.IsPublicOnly));
        Assert.All(keys, key => Assert.False(key.IsInAgent));
    }

    [Fact]
    public async Task Load_reports_orphan_public_keys()
    {
        await _service.GenerateAsync(
            new KeyGenerationRequest("id_orphan_source", KeyAlgorithm.Ed25519, 256, "orphan@test", null, false), false);

        File.Copy(
            Path.Combine(_directory, "id_orphan_source.pub"),
            Path.Combine(_directory, "orphan.pub"));

        var keys = await _service.LoadKeysAsync(new HashSet<string>(StringComparer.Ordinal));
        var orphan = Assert.Single(keys, key => key.Name == "orphan.pub");

        Assert.True(orphan.IsPublicOnly);
        Assert.True(orphan.HasPublicKey);
    }

    [Fact]
    public async Task Delete_removes_private_and_public_key_files()
    {
        var key = await _service.GenerateAsync(
            new KeyGenerationRequest("id_delete", KeyAlgorithm.Ed25519, 256, "del@test", null, false), false);

        await _service.DeleteAsync(key, deletePublicKey: true);

        Assert.False(File.Exists(Path.Combine(_directory, "id_delete")));
        Assert.False(File.Exists(Path.Combine(_directory, "id_delete.pub")));
    }

    [Fact]
    public async Task Backup_writes_a_zip_containing_the_key_files()
    {
        await _service.GenerateAsync(
            new KeyGenerationRequest("id_backup", KeyAlgorithm.Ed25519, 256, "backup@test", null, false), false);

        var zipPath = Path.Combine(_directory, "..", "backup-" + Guid.NewGuid().ToString("N") + ".zip");

        try
        {
            var created = await _service.CreateBackupAsync(zipPath);

            using var archive = System.IO.Compression.ZipFile.OpenRead(created);
            var names = archive.Entries.Select(entry => entry.FullName).ToList();

            Assert.Contains("id_backup", names);
            Assert.Contains("id_backup.pub", names);
        }
        finally
        {
            if (File.Exists(zipPath))
                File.Delete(zipPath);
        }
    }
}
