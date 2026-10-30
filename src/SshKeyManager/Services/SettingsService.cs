using System.IO;
using System.Text.Json;

namespace SshKeyManager.Services;

public sealed class AppSettings
{
    public string Theme { get; set; } = "System";
}

/// <summary>Persists small UI preferences under %APPDATA%\SshKeyManager.</summary>
public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _path;

    public SettingsService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _path = Path.Combine(appData, "SshKeyManager", "settings.json");
        Current = Load();
    }

    public AppSettings Current { get; }

    public void Save()
    {
        try
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            File.WriteAllText(_path, JsonSerializer.Serialize(Current, JsonOptions));
        }
        catch
        {
            // Preferences are optional; never block the app.
        }
    }

    private AppSettings Load()
    {
        try
        {
            if (File.Exists(_path))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path)) ?? new AppSettings();
        }
        catch
        {
            // Corrupt settings fall back to defaults.
        }

        return new AppSettings();
    }
}
