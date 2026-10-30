namespace SshKeyManager.Models;

public enum KeyAlgorithm
{
    Ed25519,
    Rsa,
    Ecdsa
}

public sealed record KeyGenerationRequest(
    string Name,
    KeyAlgorithm Algorithm,
    int Bits,
    string Comment,
    string? Passphrase,
    bool AddToAgent);

public sealed record AlgorithmOption(string Label, KeyAlgorithm Kind);
