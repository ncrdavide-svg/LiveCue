using System.Windows;
using LibVLCSharp.Shared;

namespace LiveCue.App;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    public static LibVLC? VlcInstance { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        LibVLCSharp.Shared.Core.Initialize();
        VlcInstance = new LibVLC();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        VlcInstance?.Dispose();
        base.OnExit(e);
    }
}
