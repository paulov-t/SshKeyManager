using SshKeyManager.ViewModels;

namespace SshKeyManager.Services;

/// <summary>UI operations the view model needs from the window (dialogs, clipboard, pickers).</summary>
public interface IDialogService
{
    Task ShowMessageAsync(string title, string message);

    Task<bool> ConfirmAsync(string title, string message, string confirmText = "OK", string? cancelText = null, bool danger = false);

    Task<string?> PromptAsync(string title, string message, string? defaultValue = null,
        string? watermark = null, bool password = false);

    Task<bool> ShowGenerateKeyDialogAsync(GenerateKeyViewModel viewModel);

    Task<string?> EditRawConfigAsync(string title, string initialText);

    Task<string?> PickFolderAsync(string title, string? startIn = null);

    Task<string?> PickSaveFileAsync(string title, string suggestedFileName, string extension, string typeName,
        string? startIn = null);

    Task CopyTextAsync(string text);

    void RevealInExplorer(string path);

    void OpenFolder(string path);
}
