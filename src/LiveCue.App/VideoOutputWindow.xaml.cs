using System.Windows;
using System.Windows.Interop;
using LibVLCSharp.Shared;
using LiveCue.Core.Services;

namespace LiveCue.App;

/// <summary>
/// Borderless, always-on-top window placed over a target rectangle (a physical display, or a
/// virtual display region on top of one) to show cue video output.
/// </summary>
public partial class VideoOutputWindow : Window
{
    public ScreenBounds Bounds { get; private set; }

    /// <summary>Win32 window handle, valid once the window has been shown.</summary>
    public IntPtr Handle => new WindowInteropHelper(this).Handle;

    public VideoOutputWindow(ScreenBounds bounds)
    {
        InitializeComponent();
        Bounds = bounds;
        MoveToDisplay(bounds);
    }

    public void MoveToDisplay(ScreenBounds bounds)
    {
        Bounds = bounds;
        Left = bounds.X;
        Top = bounds.Y;
        Width = bounds.Width;
        Height = bounds.Height;
    }

    public void Attach(MediaPlayer mediaPlayer) => VideoViewControl.MediaPlayer = mediaPlayer;

    public void Detach() => VideoViewControl.MediaPlayer = null;
}
