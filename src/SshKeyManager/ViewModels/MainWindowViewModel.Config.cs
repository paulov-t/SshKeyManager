using System.IO;
using CommunityToolkit.Mvvm.Input;
using SshKeyManager.Models;

namespace SshKeyManager.ViewModels;

public sealed partial class MainWindowViewModel
{
    [RelayCommand]
    private async Task SaveConfigAsync()
    {
        if (_document is null || ConfigHasUnsupportedBlocks)
            return;

        if (!ValidateHosts(out var error))
        {
            await _dialogs.ShowMessageAsync("Check the host entries", error!);
            return;
        }

        IsBusy = true;
        try
        {
            var backup = await _config.SaveAsync(_document, Hosts.ToList());
            IsConfigDirty = false;
            Status(backup is null
                ? "SSH config saved."
                : $"SSH config saved. Previous version kept as {Path.GetFileName(backup)}.");
        }
        catch (Exception ex)
        {
            await ErrorAsync("Could not save the SSH config", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ReloadConfigAsync()
    {
        if (IsConfigDirty)
        {
            var discard = await _dialogs.ConfirmAsync(
                "Discard changes",
                "Reload the config from disk and discard the changes you made here?",
                "Discard changes",
                "Cancel",
                danger: true);

            if (!discard)
                return;
        }

        LoadConfig();
        Status("SSH config reloaded from disk.");
    }

    [RelayCommand]
    private void AddHost()
    {
        if (_document is null)
            return;

        var index = 1;
        string alias;
        do
        {
            alias = index == 1 ? "new-host" : $"new-host-{index}";
            index++;
        } while (Hosts.Any(h => string.Equals(h.Alias, alias, StringComparison.OrdinalIgnoreCase)));

        var host = new SshHostEntry { Alias = alias };
        host.PropertyChanged += OnHostPropertyChanged;
        Hosts.Add(host);
        SelectedHost = host;
        IsConfigDirty = true;
        OnPropertyChanged(nameof(HasHosts));
        Status("Added a new host. Fill in the details and save the config.");
    }

    [RelayCommand]
    private async Task DeleteHostAsync()
    {
        var host = SelectedHost;
        if (host is null)
            return;

        var confirmed = await _dialogs.ConfirmAsync(
            "Remove host",
            $"Remove the host entry \"{host.DisplayName}\" from ~/.ssh/config?",
            "Remove",
            "Cancel",
            danger: true);

        if (!confirmed)
            return;

        var index = Hosts.IndexOf(host);
        host.PropertyChanged -= OnHostPropertyChanged;
        Hosts.Remove(host);
        SelectedHost = Hosts.Count == 0 ? null : Hosts[Math.Clamp(index, 0, Hosts.Count - 1)];
        IsConfigDirty = true;
        OnPropertyChanged(nameof(HasHosts));
        Status($"Removed host {host.DisplayName}.");
    }

    [RelayCommand]
    private void UseIdentity()
    {
        if (SelectedHost is null || string.IsNullOrWhiteSpace(SelectedIdentityOption))
            return;

        SelectedHost.IdentityFile = SelectedIdentityOption;
        Status($"Identity file set to {SelectedIdentityOption}.");
    }

    [RelayCommand]
    private async Task EditRawConfigAsync()
    {
        if (IsConfigDirty)
        {
            var discard = await _dialogs.ConfirmAsync(
                "Unsaved changes",
                "The form has unsaved changes. Opening the raw editor will discard them. Continue?",
                "Open raw editor",
                "Cancel",
                danger: true);

            if (!discard)
                return;
        }

        string initial;
        try
        {
            _keys.EnsureDirectory();
            initial = File.Exists(ConfigPath) ? await File.ReadAllTextAsync(ConfigPath) : string.Empty;
        }
        catch (Exception ex)
        {
            await ErrorAsync("Could not read the SSH config", ex);
            return;
        }

        var edited = await _dialogs.EditRawConfigAsync("Edit ~/.ssh/config", initial);
        if (edited is null)
            return;
        try
        {
            var backup = await _config.WriteRawAsync(edited);
            LoadConfig();
            Status(backup is null
                ? "SSH config written."
                : $"SSH config written. Previous version kept as {Path.GetFileName(backup)}.");
        }
        catch (Exception ex)
        {
            await ErrorAsync("Could not write the SSH config", ex);
        }
    }

    private bool ValidateHosts(out string? error)
    {
        foreach (var host in Hosts)
        {
            if (string.IsNullOrWhiteSpace(host.Alias))
            {
                error = "Every host needs a Host alias.";
                return false;
            }

            if (host.Alias.Contains('\n') || host.Alias.Contains('\r'))
            {
                error = $"The alias \"{host.Alias}\" contains a line break.";
                return false;
            }

            if (!string.IsNullOrWhiteSpace(host.Port))
            {
                if (!int.TryParse(host.Port.Trim(), out var port) || port is < 1 or > 65535)
                {
                    error = $"The port for \"{host.Alias}\" must be a number between 1 and 65535.";
                    return false;
                }
            }
        }

        error = null;
        return true;
    }
}
