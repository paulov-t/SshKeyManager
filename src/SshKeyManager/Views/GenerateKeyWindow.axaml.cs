using System;
using Avalonia.Controls;
using SshKeyManager.ViewModels;

namespace SshKeyManager.Views;

public partial class GenerateKeyWindow : Window
{
    private GenerateKeyViewModel? _viewModel;

    public GenerateKeyWindow()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_viewModel is not null)
            _viewModel.CloseRequested -= OnCloseRequested;

        _viewModel = DataContext as GenerateKeyViewModel;

        if (_viewModel is not null)
            _viewModel.CloseRequested += OnCloseRequested;
    }

    private void OnCloseRequested(object? sender, bool created) => Close(created);
}
