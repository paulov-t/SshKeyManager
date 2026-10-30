using Avalonia.Controls;
using SshKeyManager.ViewModels;

namespace SshKeyManager.Views;

public partial class MainWindow : Window
{
    private bool _closeConfirmed;

    public MainWindow()
    {
        InitializeComponent();
    }

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        if (_closeConfirmed || DataContext is not MainWindowViewModel viewModel || !viewModel.IsConfigDirty)
        {
            base.OnClosing(e);
            return;
        }

        e.Cancel = true;

        if (await viewModel.ConfirmExitWithUnsavedChangesAsync())
        {
            _closeConfirmed = true;
            Close();
        }
    }
}
