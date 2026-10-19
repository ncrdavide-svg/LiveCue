using System.Windows;
using System.Windows.Interop;
using LibVLCSharp.Shared;

namespace LiveCue.App;

/// <summary>
/// Hosts LibVLC's video surface off-screen, purely so MediaPlayer.TakeSnapshot has an active
/// video output to capture from - libvlc needs one even though nothing here is ever meant to be
/// seen. Positioned far outside any real monitor's bounds rather than just made invisible, since
/// an invisible-but-on-screen window can still briefly flash during creation on some systems.
/// </summary>
public partial class ThumbnailCaptureWindow : Window
{
    public ThumbnailCaptureWindow()
    {
        InitializeComponent();
        Left = -32000;
        Top = -32000;
        Width = 320;
        Height = 180;

        SourceInitialized += (_, _) => Win32WindowPlacement.SetNoActivate(new WindowInteropHelper(this).Handle);
    }

    public void Attach(MediaPlayer mediaPlayer) => VideoViewControl.MediaPlayer = mediaPlayer;
}
