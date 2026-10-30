using System;
using Avalonia;

namespace SshKeyManager;

internal sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        Services.CrashLogger.Install();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration; also used by the visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
