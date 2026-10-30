using System.Collections.ObjectModel;
using System.IO;
using Avalonia;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using SshKeyManager.Models;
using SshKeyManager.Services;

namespace SshKeyManager.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly SshKeyService _keys;
    private readonly SshConfigService _config;
    private readonly SettingsService _settings;
    private readonly IDialogService _dialogs;

    private SshConfigDocument? _document;
    private bool _initialized;
    private bool _suppressDirty;

    public MainWindowViewModel(
        SshKeyService keys,
        SshConfigService config,
        SettingsService settings,
        IDialogService dialogs)
    {
        _keys = keys;
        _config = config;
        _settings = settings;
        _dialogs = dialogs;

        ThemeLabel = settings.Current.Theme switch
        {
            "Light" => "Theme: light",
            "Dark" => "Theme: dark",
            _ => "Theme: system"
        };
    }

    public string SshDirectory => _keys.SshDirectory;
    public string ConfigPath => _config.ConfigPath;

    public ObservableCollection<SshKeyInfo> Keys { get; } = new();
    public ObservableCollection<SshKeyInfo> FilteredKeys { get; } = new();
    public ObservableCollection<SshHostEntry> Hosts { get; } = new();
    public ObservableCollection<string> Log { get; } = new();
    public ObservableCollection<string> IdentityOptions { get; } = new();

    public IReadOnlyList<string> TestTargets { get; } = new[] { "GitHub", "GitLab", "Bitbucket", "Codeberg", "Custom…" };

    public string AppVersion =>
        typeof(MainWindowViewModel).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";

    [ObservableProperty] private string searchText = string.Empty;
    [ObservableProperty] private SshKeyInfo? selectedKey;
    [ObservableProperty] private SshHostEntry? selectedHost;
    [ObservableProperty] private string statusMessage = "Starting…";
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private bool agentRunning;
    [ObservableProperty] private string agentStatusText = "Checking ssh-agent…";
    [ObservableProperty] private string agentTooltip = string.Empty;
    [ObservableProperty] private bool isConfigDirty;
    [ObservableProperty] private bool configHasUnsupportedBlocks;
    [ObservableProperty] private string themeLabel = "Theme: system";
    [ObservableProperty] private string selectedTestTarget = "GitHub";
    [ObservableProperty] private string customTestTarget = string.Empty;
    [ObservableProperty] private string selectedIdentityOption = string.Empty;

    [ObservableProperty] private bool selectedKeyIsInAgent;
    [ObservableProperty] private bool selectedKeyCanAddToAgent;
    [ObservableProperty] private bool selectedKeyHasPublicKey;
    [ObservableProperty] private bool selectedKeyIsEncrypted;
    [ObservableProperty] private bool selectedKeyIsPublicOnly;

    public bool HasSelection => SelectedKey is not null;
    public bool HasNoSelection => SelectedKey is null;
    public bool HasKeys => Keys.Count > 0;
    public bool HasNoKeys => Keys.Count == 0;
    public bool HasSelectedHost => SelectedHost is not null;
    public bool HasHosts => Hosts.Count > 0;
    public bool AgentWarning => !AgentRunning;
    public bool IsCustomTestTarget => SelectedTestTarget == "Custom…";
    public bool CanTestKey => SelectedKey is { IsPublicOnly: false };
    public string SshFolderLabel => SshDirectory;

    public async Task InitializeAsync()
    {
        if (_initialized)
            return;
        _initialized = true;

        if (SshTools.MissingTools is { } missing)
        {
            StatusMessage = "OpenSSH tools missing: " + missing;
            LogLine("OpenSSH client not found (" + missing + "). Install it to generate or test keys.");
        }
        else
        {
            LogLine("OpenSSH client detected.");
        }

        LoadConfig();
        await RefreshAsync();

        if (!AgentRunning)
            LogLine("ssh-agent is not running. Keys can still be managed, but agent actions are unavailable.");
    }

    partial void OnSelectedKeyChanged(SshKeyInfo? value)
    {
        RefreshSelectedKeyFlags();
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(HasNoSelection));
        OnPropertyChanged(nameof(CanTestKey));
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    partial void OnSelectedHostChanged(SshHostEntry? value) => OnPropertyChanged(nameof(HasSelectedHost));

    partial void OnAgentRunningChanged(bool value) => OnPropertyChanged(nameof(AgentWarning));

    partial void OnSelectedTestTargetChanged(string value) => OnPropertyChanged(nameof(IsCustomTestTarget));

    private void RefreshSelectedKeyFlags()
    {
        var key = SelectedKey;
        SelectedKeyIsPublicOnly = key?.IsPublicOnly ?? false;
        SelectedKeyIsEncrypted = key?.IsEncrypted ?? false;
        SelectedKeyIsInAgent = key?.IsInAgent ?? false;
        SelectedKeyHasPublicKey = !string.IsNullOrWhiteSpace(key?.PublicKeyText);
        SelectedKeyCanAddToAgent = key is { IsPublicOnly: false, IsInAgent: false };
    }

    private void ApplyFilter()
    {
        var query = SearchText.Trim();
        FilteredKeys.Clear();

        foreach (var key in Keys)
        {
            if (query.Length == 0 ||
                key.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                key.Comment.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                key.Sha256Fingerprint.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                key.Algorithm.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                FilteredKeys.Add(key);
            }
        }

        if (SelectedKey is not null && !FilteredKeys.Contains(SelectedKey))
            SelectedKey = FilteredKeys.FirstOrDefault();
        else if (SelectedKey is null)
            SelectedKey = FilteredKeys.FirstOrDefault();
    }

    private void BuildIdentityOptions()
    {
        IdentityOptions.Clear();
        foreach (var key in Keys.Where(k => !k.IsPublicOnly))
            IdentityOptions.Add("~/.ssh/" + key.Name);
    }

    private void LoadConfig()
    {
        foreach (var host in Hosts)
            host.PropertyChanged -= OnHostPropertyChanged;

        _document = _config.Load();
        ConfigHasUnsupportedBlocks = _document.HasUnsupportedBlocks;

        _suppressDirty = true;
        Hosts.Clear();

        if (!ConfigHasUnsupportedBlocks)
        {
            foreach (var host in _document.Hosts)
            {
                host.PropertyChanged += OnHostPropertyChanged;
                Hosts.Add(host);
            }
        }

        SelectedHost = Hosts.FirstOrDefault();
        _suppressDirty = false;
        IsConfigDirty = false;

        OnPropertyChanged(nameof(HasHosts));
        LogLine(ConfigHasUnsupportedBlocks
            ? "SSH config contains Match blocks — use the raw editor for this file."
            : $"Loaded SSH config ({Hosts.Count} host(s)).");
    }

    private void OnHostPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (!_suppressDirty)
            IsConfigDirty = true;
    }

    private void LogLine(string message)
    {
        Log.Insert(0, $"{DateTime.Now:HH:mm:ss}  {message}");
        while (Log.Count > 200)
            Log.RemoveAt(Log.Count - 1);
    }

    private void Status(string message)
    {
        StatusMessage = message;
        LogLine(message);
    }

    private async Task ErrorAsync(string context, Exception exception)
    {
        StatusMessage = context + ": " + exception.Message;
        LogLine("✗ " + context + ": " + exception.Message);
        await _dialogs.ShowMessageAsync(context, exception.Message);
    }

    public async Task<bool> ConfirmExitWithUnsavedChangesAsync()
    {
        if (!IsConfigDirty)
            return true;

        return await _dialogs.ConfirmAsync(
            "Unsaved changes",
            "Your SSH config has changes that were not saved. Exit and discard them?",
            "Discard and exit",
            "Stay",
            danger: true);
    }

    private static string MapTarget(string selection, string custom)
    {
        return selection switch
        {
            "GitHub" => "git@github.com",
            "GitLab" => "git@gitlab.com",
            "Bitbucket" => "git@bitbucket.org",
            "Codeberg" => "git@codeberg.org",
            _ => custom.Trim()
        };
    }

    private static bool ApplyThemeVariant(string theme)
    {
        if (Application.Current is null)
            return false;

        Application.Current.RequestedThemeVariant = theme switch
        {
            "Light" => ThemeVariant.Light,
            "Dark" => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };

        return true;
    }
}
