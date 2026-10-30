using System.IO;
using CommunityToolkit.Mvvm.Input;
using SshKeyManager.Services;

namespace SshKeyManager.ViewModels;

public sealed partial class MainWindowViewModel
{
    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (IsBusy)
            return;

        IsBusy = true;
        try
        {
            StatusMessage = "Scanning ~/.ssh…";
            _keys.EnsureDirectory();

            var agent = await SshAgentService.SnapshotAsync();
            AgentRunning = agent.Running;
            AgentTooltip = agent.Error ?? "ssh-agent is reachable.";
            AgentStatusText = agent.Running
                ? agent.Keys.Count == 0
                    ? "ssh-agent · no keys loaded"
                    : $"ssh-agent · {agent.Keys.Count} loaded"
                : "ssh-agent · not running";

            var loaded = await _keys.LoadKeysAsync(agent.Keys.ToHashSet(StringComparer.Ordinal));
            var previous = SelectedKey?.PrivatePath;

            Keys.Clear();
            foreach (var key in loaded)
                Keys.Add(key);

            BuildIdentityOptions();
            ApplyFilter();

            SelectedKey =
                (previous is null
                    ? null
                    : Keys.FirstOrDefault(k => string.Equals(k.PrivatePath, previous, StringComparison.OrdinalIgnoreCase)))
                ?? FilteredKeys.FirstOrDefault();

            RefreshSelectedKeyFlags();
            OnPropertyChanged(nameof(HasKeys));
            OnPropertyChanged(nameof(HasNoKeys));

            StatusMessage = loaded.Count == 0 ? "No SSH keys found in ~/.ssh" : $"{loaded.Count} key(s) found";
            LogLine($"Refreshed: {loaded.Count} key(s); {AgentStatusText}.");
        }
        catch (Exception ex)
        {
            await ErrorAsync("Refresh failed", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task NewKeyAsync()
    {
        var dialog = new GenerateKeyViewModel(_keys);
        var created = await _dialogs.ShowGenerateKeyDialogAsync(dialog);

        if (!created || dialog.CreatedKey is null)
            return;

        SearchText = string.Empty;
        await RefreshAsync();

        var match = Keys.FirstOrDefault(k =>
            string.Equals(k.Name, dialog.CreatedKey.Name, StringComparison.OrdinalIgnoreCase));
        if (match is not null)
            SelectedKey = match;

        Status($"Created {dialog.CreatedKey.Name}.");

        if (_keys.LastGenerationWarning is { } warning)
            await _dialogs.ShowMessageAsync("Key created", warning);
    }

    [RelayCommand]
    private async Task CopyPublicKeyAsync()
    {
        var key = SelectedKey;
        if (key is null)
            return;

        if (string.IsNullOrWhiteSpace(key.PublicKeyText))
        {
            await _dialogs.ShowMessageAsync("No public key",
                "This key has no readable public key. If it is passphrase protected, add a .pub file or load it in ssh-agent first.");
            return;
        }

        await _dialogs.CopyTextAsync(key.PublicKeyText.Trim());
        Status($"Copied the public key for {key.Name} to the clipboard.");
    }

    [RelayCommand]
    private async Task CopyFingerprintAsync()
    {
        var key = SelectedKey;
        if (key is null || string.IsNullOrWhiteSpace(key.Sha256Fingerprint))
            return;

        await _dialogs.CopyTextAsync(key.Sha256Fingerprint);
        Status($"Copied the SHA256 fingerprint for {key.Name}.");
    }

    [RelayCommand]
    private async Task ExportPublicKeyAsync()
    {
        var key = SelectedKey;
        if (key is null)
            return;

        if (string.IsNullOrWhiteSpace(key.PublicKeyText))
        {
            await _dialogs.ShowMessageAsync("No public key", "There is no public key text to export for this key.");
            return;
        }

        var destination = await _dialogs.PickSaveFileAsync(
            "Export public key",
            Path.GetFileNameWithoutExtension(key.Name) + ".pub",
            "pub",
            "Public key",
            SshDirectory);

        if (destination is null)
            return;

        try
        {
            await _keys.ExportPublicKeyAsync(key, destination);
            Status($"Exported {key.Name} to {destination}.");
        }
        catch (Exception ex)
        {
            await ErrorAsync("Export failed", ex);
        }
    }

    [RelayCommand]
    private async Task AddToAgentAsync()
    {
        var key = SelectedKey;
        if (key is null || key.IsPublicOnly)
            return;

        string? passphrase = null;
        if (key.IsEncrypted)
        {
            passphrase = await _dialogs.PromptAsync(
                "Passphrase required",
                $"Enter the passphrase for {key.Name}. It is sent to ssh-add over stdin and is never stored.",
                password: true);

            if (passphrase is null)
                return;
        }

        var added = false;
        IsBusy = true;
        try
        {
            var result = await SshAgentService.AddAsync(key.PrivatePath, passphrase);
            if (result.ExitCode == 0)
            {
                key.IsInAgent = true;
                added = true;
                Status($"{key.Name} added to ssh-agent.");
            }
            else
            {
                var message = SshAgentService.FriendlyError(result, key.IsEncrypted);
                Status("ssh-agent could not load the key.");
                LogLine("✗ ssh-add: " + message.ReplaceLineEndings(" "));
                await _dialogs.ShowMessageAsync("ssh-agent", message);
            }
        }
        catch (Exception ex)
        {
            await ErrorAsync("ssh-add failed", ex);
        }
        finally
        {
            IsBusy = false;
        }

        if (added)
            await RefreshAsync();
    }

    [RelayCommand]
    private async Task RemoveFromAgentAsync()
    {
        var key = SelectedKey;
        if (key is null)
            return;

        IsBusy = true;
        var removed = false;
        try
        {
            var result = await SshAgentService.RemoveAsync(key.PrivatePath);
            if (result.ExitCode == 0)
            {
                key.IsInAgent = false;
                removed = true;
                Status($"{key.Name} removed from ssh-agent.");
            }
            else
            {
                await _dialogs.ShowMessageAsync("ssh-agent", SshAgentService.FriendlyError(result, key.IsEncrypted));
            }
        }
        catch (Exception ex)
        {
            await ErrorAsync("ssh-add -d failed", ex);
        }
        finally
        {
            IsBusy = false;
        }

        if (removed)
            await RefreshAsync();
    }

    [RelayCommand]
    private async Task TestKeyAsync()
    {
        var key = SelectedKey;
        if (key is null || key.IsPublicOnly)
            return;

        var target = MapTarget(SelectedTestTarget, CustomTestTarget);

        if (string.IsNullOrWhiteSpace(target))
        {
            target = await _dialogs.PromptAsync(
                "Custom target",
                "Enter the SSH target to test, for example git@github.com",
                "git@github.com") ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(target))
            return;

        IsBusy = true;
        try
        {
            StatusMessage = $"Testing {key.Name} against {target}…";
            var result = await SshConnectionTester.TestAsync(key.PrivatePath, target);

            LogLine((result.Success ? "✓ " : "✗ ") + $"{target}: {result.Summary}");

            var body = result.Summary + Environment.NewLine + Environment.NewLine + result.Details;
            if (!string.IsNullOrWhiteSpace(result.RawOutput))
                body += Environment.NewLine + Environment.NewLine + result.RawOutput;

            await _dialogs.ShowMessageAsync(
                result.Success ? "Connection successful" : "Connection failed",
                body);

            StatusMessage = result.Success
                ? $"Key {key.Name} works with {target}."
                : $"Test against {target} failed.";
        }
        catch (Exception ex)
        {
            await ErrorAsync("Connection test failed", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task DeleteKeyAsync()
    {
        var key = SelectedKey;
        if (key is null)
            return;

        var backupPath = _keys.DefaultBackupPath();
        var confirmed = await _dialogs.ConfirmAsync(
            "Delete key",
            $"Delete {key.Name} from ~/.ssh?\n\nA safety backup of the whole .ssh folder will be written to:\n{backupPath}\n\nThis cannot be undone.",
            "Delete key",
            "Cancel",
            danger: true);

        if (!confirmed)
            return;

        IsBusy = true;
        try
        {
            await _keys.CreateBackupAsync(backupPath);
            await _keys.DeleteAsync(key, deletePublicKey: true);
            Status($"Deleted {key.Name}. Backup: {backupPath}");
        }
        catch (Exception ex)
        {
            await ErrorAsync("Delete failed", ex);
        }
        finally
        {
            IsBusy = false;
        }

        await RefreshAsync();
    }

    [RelayCommand]
    private void ShowInExplorer()
    {
        var key = SelectedKey;
        if (key is not null)
            _dialogs.RevealInExplorer(key.PrivatePath);
    }

    [RelayCommand]
    private void OpenSshFolder()
    {
        _keys.EnsureDirectory();
        _dialogs.OpenFolder(SshDirectory);
    }

    [RelayCommand]
    private async Task BackupAsync()
    {
        var suggested = Path.GetFileName(_keys.DefaultBackupPath());
        var destination = await _dialogs.PickSaveFileAsync(
            "Back up ~/.ssh",
            suggested,
            "zip",
            "Zip archive",
            _keys.BackupDirectory);

        if (destination is null)
            return;

        IsBusy = true;
        try
        {
            await _keys.CreateBackupAsync(destination);
            Status($"Backup written to {destination}.");
            await _dialogs.ShowMessageAsync("Backup complete",
                "A backup of " + SshDirectory + " was written to:" + Environment.NewLine + destination);
        }
        catch (Exception ex)
        {
            await ErrorAsync("Backup failed", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void ToggleTheme()
    {
        var next = _settings.Current.Theme switch
        {
            "System" => "Light",
            "Light" => "Dark",
            _ => "System"
        };

        _settings.Current.Theme = next;
        _settings.Save();
        ApplyThemeVariant(next);

        ThemeLabel = next switch
        {
            "Light" => "Theme: light",
            "Dark" => "Theme: dark",
            _ => "Theme: system"
        };

        LogLine($"Theme set to {next.ToLowerInvariant()}.");
    }

    [RelayCommand]
    private async Task StartAgentAsync()
    {
        if (!OperatingSystem.IsWindows())
        {
            await _dialogs.ShowMessageAsync("ssh-agent",
                "Start ssh-agent with your system service manager, for example: eval \"$(ssh-agent -s)\"");
            return;
        }

        IsBusy = true;
        try
        {
            var result = await SshAgentService.TryStartServiceAsync();
            if (result.ExitCode == 0)
            {
                Status("ssh-agent service started.");
                await RefreshAsync();
            }
            else
            {
                await _dialogs.ShowMessageAsync("Could not start ssh-agent",
                    result.Combined + Environment.NewLine + Environment.NewLine +
                    "Starting the service usually requires an elevated shell. Open PowerShell as administrator and run:" +
                    Environment.NewLine + "Start-Service ssh-agent" + Environment.NewLine +
                    "You can also set the service to start automatically in services.msc.");
            }
        }
        catch (Exception ex)
        {
            await ErrorAsync("Could not start ssh-agent", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
