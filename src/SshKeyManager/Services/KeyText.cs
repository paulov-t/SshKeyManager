namespace SshKeyManager.Services;

/// <summary>Normalised public-key text used to match local keys against ssh-agent entries.</summary>
public static class KeyText
{
    public static string Normalize(string? publicKeyText)
    {
        if (string.IsNullOrWhiteSpace(publicKeyText))
            return string.Empty;

        var parts = publicKeyText.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
            return string.Empty;

        return parts[0] + " " + parts[1];
    }

    public static string Comment(string? publicKeyText)
    {
        if (string.IsNullOrWhiteSpace(publicKeyText))
            return string.Empty;

        var parts = publicKeyText.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length <= 2 ? string.Empty : string.Join(' ', parts.Skip(2));
    }
}
