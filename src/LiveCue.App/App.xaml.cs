using System.IO;
using System.Windows;
using System.Windows.Threading;
using LibVLCSharp.Shared;

namespace LiveCue.App;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    public static LibVLC? VlcInstance { get; private set; }

    /// <summary>%LOCALAPPDATA%\LiveCue\errors.log — every unexpected error is appended here.</summary>
    public static string ErrorLogPath { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LiveCue", "errors.log");

    protected override void OnStartup(StartupEventArgs e)
    {
        // A show-control app must never close in the middle of a show because of one bad cue:
        // unexpected errors are logged and reported in the status bar, and the app keeps running.
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            LogError(args.Exception);
            args.SetObserved();
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex) LogError(ex);
        };

        base.OnStartup(e);
        LibVLCSharp.Shared.Core.Initialize();
        VlcInstance = new LibVLC();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        VlcInstance?.Dispose();
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogError(e.Exception);
        e.Handled = true;

        var message = $"Errore: {e.Exception.Message} (dettagli in {ErrorLogPath})";
        if (MainWindow?.DataContext is MainViewModel viewModel)
            viewModel.StatusMessage = message;
        else
            MessageBox.Show(message, "LiveCue", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    public static void LogError(Exception exception)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ErrorLogPath)!);
            File.AppendAllText(ErrorLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
            // logging must never throw
        }
    }
}
