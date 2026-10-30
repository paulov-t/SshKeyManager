using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SshKeyManager.Views;

public partial class RawConfigWindow : Window
{
    public RawConfigWindow()
    {
        InitializeComponent();
    }

    public RawConfigWindow(string title, string initialText) : this()
    {
        Title = title;
        HeadingText.Text = title;
        Editor.Text = initialText;
        Editor.CaretIndex = initialText.Length;
    }

    private void OnSave(object? sender, RoutedEventArgs e) => Close(Editor.Text ?? string.Empty);

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);
}
