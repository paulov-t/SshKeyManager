using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SshKeyManager.Views;

public partial class MessageDialog : Window
{
    public MessageDialog()
    {
        InitializeComponent();
    }

    public MessageDialog(string title, string message, string confirmText, string? cancelText, bool danger)
        : this()
    {
        Title = title;
        TitleText.Text = title;
        MessageText.Text = message;
        ConfirmButton.Content = confirmText;

        if (cancelText is null)
            CancelButton.IsVisible = false;
        else
            CancelButton.Content = cancelText;

        if (danger)
        {
            ConfirmButton.Classes.Remove("primary");
            ConfirmButton.Classes.Add("danger");
        }
    }

    private void OnConfirm(object? sender, RoutedEventArgs e) => Close(true);

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
