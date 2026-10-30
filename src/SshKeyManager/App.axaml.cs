using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using SshKeyManager.Services;
using SshKeyManager.ViewModels;
using SshKeyManager.Views;

namespace SshKeyManager;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var settings = new SettingsService();
            ApplyTheme(settings.Current.Theme);

            var window = new MainWindow();
            var dialogs = new WindowDialogService(window);
            var viewModel = new MainWindowViewModel(
                new SshKeyService(),
                new SshConfigService(),
                settings,
                dialogs);

            window.DataContext = viewModel;
            desktop.MainWindow = window;

            window.Opened += async (_, _) =>
            {
                try
                {
                    await viewModel.InitializeAsync();
                }
                catch (Exception ex)
                {
                    await dialogs.ShowMessageAsync("Startup error", ex.Message);
                }
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void ApplyTheme(string theme)
    {
        RequestedThemeVariant = theme switch
        {
            "Light" => ThemeVariant.Light,
            "Dark" => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };
    }
}
