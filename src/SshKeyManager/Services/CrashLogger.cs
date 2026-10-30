using System.IO;
using System.Text;

namespace SshKeyManager.Services;

/// <summary>Writes unexpected errors to %APPDATA%\SshKeyManager\error.log so failures are diagnosable.</summary>
public static class CrashLogger
{
    private static readonly object Gate = new();

    public static string LogPath
    {
        get
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "SshKeyManager", "error.log");
        }
    }

    public static void Install()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Write("AppDomain.UnhandledException", args.ExceptionObject as Exception);

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Write("UnobservedTaskException", args.Exception);
            args.SetObserved();
        };
    }

    public static void Write(string source, Exception? exception)
    {
        if (exception is null)
            return;

        try
        {
            var builder = new StringBuilder();
            builder.AppendLine($"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}] {source}");
            builder.AppendLine(exception.ToString());
            builder.AppendLine();

            lock (Gate)
            {
                var directory = Path.GetDirectoryName(LogPath);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);
                File.AppendAllText(LogPath, builder.ToString());
            }
        }
        catch
        {
            // Logging must never throw.
        }
    }
}
