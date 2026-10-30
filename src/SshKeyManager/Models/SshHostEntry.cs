using CommunityToolkit.Mvvm.ComponentModel;

namespace SshKeyManager.Models;

/// <summary>A single <c>Host</c> block from ~/.ssh/config, surfaced as editable fields.</summary>
public sealed partial class SshHostEntry : ObservableObject
{
    [ObservableProperty]
    private string alias = string.Empty;

    [ObservableProperty]
    private string hostName = string.Empty;

    [ObservableProperty]
    private string user = string.Empty;

    [ObservableProperty]
    private string port = string.Empty;

    [ObservableProperty]
    private string identityFile = string.Empty;

    [ObservableProperty]
    private bool identitiesOnly;

    /// <summary>All other directives (and comments) kept verbatim.</summary>
    [ObservableProperty]
    private string extraOptions = string.Empty;

    public string DisplayName => string.IsNullOrWhiteSpace(Alias) ? "(new host)" : Alias.Trim();

    public string Summary
    {
        get
        {
            var host = string.IsNullOrWhiteSpace(HostName) ? "no hostname" : HostName.Trim();
            var userPart = string.IsNullOrWhiteSpace(User) ? host : $"{User.Trim()}@{host}";
            return string.IsNullOrWhiteSpace(Port) ? userPart : $"{userPart}:{Port.Trim()}";
        }
    }

    partial void OnAliasChanged(string value)
    {
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(Summary));
    }

    partial void OnHostNameChanged(string value) => OnPropertyChanged(nameof(Summary));
    partial void OnUserChanged(string value) => OnPropertyChanged(nameof(Summary));
    partial void OnPortChanged(string value) => OnPropertyChanged(nameof(Summary));
}
