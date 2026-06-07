using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace SpectrumViewerRT;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        base.OnStartup(e);
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogException(e.Exception);
        e.Handled = true;
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
            LogException(exception);
    }

    private static void LogException(Exception exception)
    {
        try
        {
            var directory = Path.GetDirectoryName(AppSettings.SettingsPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);
            File.AppendAllText(Path.Combine(directory ?? ".", "error.log"), $"{DateTime.Now:u}\r\n{exception}\r\n\r\n");
        }
        catch
        {
        }
    }
}
