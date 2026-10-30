using CommunityToolkit.Mvvm.ComponentModel;

namespace SshKeyManager.Models;

/// <summary>Describes one keypair (or orphan public key) found in ~/.ssh.</summary>
public sealed partial class SshKeyInfo : ObservableObject
{
    public required string Name { get; init; }

    /// <summary>Path of the private key, or the .pub file for public-only entries.</summary>
    public required string PrivatePath { get; init; }

    public string? PublicPath { get; init; }
    public bool HasPublicKey { get; init; }
    public bool IsPublicOnly { get; init; }
    public required string Algorithm { get; init; }
    public int Bits { get; init; }
    public string Comment { get; init; } = string.Empty;
    public string Sha256Fingerprint { get; init; } = string.Empty;
    public string Md5Fingerprint { get; init; } = string.Empty;
    public string PublicKeyText { get; init; } = string.Empty;
    public bool IsEncrypted { get; init; }
    public long SizeBytes { get; init; }
    public DateTime ModifiedUtc { get; init; }

    [ObservableProperty]
    private bool isInAgent;

    public string TypeLabel => Bits > 0 ? $"{Algorithm} · {Bits} bit" : Algorithm;

    public string FingerprintShort
    {
        get
        {
            var fp = Sha256Fingerprint;
            if (fp.StartsWith("SHA256:", StringComparison.OrdinalIgnoreCase))
                fp = fp[7..];
            return fp.Length > 24 ? fp[..24] + "…" : fp;
        }
    }

    public string SizeLabel => SizeBytes >= 1024 ? $"{SizeBytes / 1024.0:0.0} KB" : $"{SizeBytes} B";
    public string ModifiedLabel => ModifiedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");

    public string PrivacyLabel => IsPublicOnly
        ? "public key only"
        : IsEncrypted ? "passphrase protected" : "no passphrase";
}
