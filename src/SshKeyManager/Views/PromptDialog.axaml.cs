using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SshKeyManager.Views;

public partial class PromptDialog : Window
{
    public PromptDialog()
    {
        InitializeComponent();
    }

    public PromptDialog(
        string title,
        string message,
        string? defaultValue = null,
        string? watermark = null,
        bool password = false)
        : this()
    {
        Title = title;
        TitleText.Text = title;
        MessageText.Text = message;
        InputBox.Text = defaultValue;
        InputBox.PlaceholderText = watermark;
        InputBox.PasswordChar = password ? '●' : '\0';
        ConfirmButton.IsEnabled = !string.IsNullOrWhiteSpace(defaultValue);
    }

    private void OnTextChanged(object? sender, TextChangedEventArgs e) =>
        ConfirmButton.IsEnabled = !string.IsNullOrWhiteSpace(InputBox.Text);

    private void OnConfirm(object? sender, RoutedEventArgs e) => Close(InputBox.Text);

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);
}
