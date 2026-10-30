using System.Diagnostics;
using System.IO;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using SshKeyManager.ViewModels;
using SshKeyManager.Views;

namespace SshKeyManager.Services;

/// <summary>Implements <see cref="IDialogService"/> using real Avalonia windows and pickers.</summary>
public sealed class WindowDialogService : IDialogService
{
    private readonly Window _owner;

    public WindowDialogService(Window owner) => _owner = owner;

    public async Task ShowMessageAsync(string title, string message)
    {
        var dialog = new MessageDialog(title, message, "OK", null, false);
        await dialog.ShowDialog<bool>(_owner);
    }

    public async Task<bool> ConfirmAsync(
        string title,
        string message,
        string confirmText = "OK",
        string? cancelText = null,
        bool danger = false)
    {
        var dialog = new MessageDialog(title, message, confirmText, cancelText ?? "Cancel", danger);
        return await dialog.ShowDialog<bool>(_owner);
    }

    public async Task<string?> PromptAsync(
        string title,
        string message,
        string? defaultValue = null,
        string? watermark = null,
        bool password = false)
    {
        var dialog = new PromptDialog(title, message, defaultValue, watermark, password);
        return await dialog.ShowDialog<string?>(_owner);
    }

    public async Task<bool> ShowGenerateKeyDialogAsync(GenerateKeyViewModel viewModel)
    {
        var dialog = new GenerateKeyWindow { DataContext = viewModel };
        return await dialog.ShowDialog<bool>(_owner);
    }

    public async Task<string?> EditRawConfigAsync(string title, string initialText)
    {
        try
        {
            var dialog = new RawConfigWindow(title, initialText);
            return await dialog.ShowDialog<string?>(_owner);
        }
        catch (Exception ex)
        {
            CrashLogger.Write("RawConfigWindow", ex);
            throw;
        }
    }

    public async Task<string?> PickFolderAsync(string title, string? startIn = null)
    {
        var folders = await _owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            SuggestedStartLocation = await TryGetFolderAsync(startIn)
        });

        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }

    public async Task<string?> PickSaveFileAsync(
        string title,
        string suggestedFileName,
        string extension,
        string typeName,
        string? startIn = null)
    {
        var file = await _owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedFileName,
            DefaultExtension = extension,
            ShowOverwritePrompt = true,
            FileTypeChoices = new List<FilePickerFileType>
            {
                new(typeName) { Patterns = new[] { "*." + extension } }
            },
            SuggestedStartLocation = await TryGetFolderAsync(startIn)
        });

        return file?.TryGetLocalPath();
    }

    public async Task CopyTextAsync(string text)
    {
        var clipboard = _owner.Clipboard;
        if (clipboard is null)
            throw new InvalidOperationException("The system clipboard is not available.");

        var data = new Avalonia.Input.DataTransfer();
        data.Add(Avalonia.Input.DataTransferItem.CreateText(text));
        await clipboard.SetDataAsync(data);
    }

    public void RevealInExplorer(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
            return;

        try
        {
            if (OperatingSystem.IsWindows() && File.Exists(path))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
                return;
            }

            var folder = File.Exists(path) ? Path.GetDirectoryName(path) : path;
            if (!string.IsNullOrEmpty(folder))
                OpenFolder(folder);
        }
        catch
        {
            // Explorer integration is best effort.
        }
    }

    public void OpenFolder(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch
        {
            // Best effort.
        }
    }

    private async Task<IStorageFolder?> TryGetFolderAsync(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        try
        {
            Directory.CreateDirectory(path);
            return await _owner.StorageProvider.TryGetFolderFromPathAsync(path);
        }
        catch
        {
            return null;
        }
    }
}
