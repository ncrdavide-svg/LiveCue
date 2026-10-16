using System.Windows;
using System.Windows.Interop;
using LiveCue.Core.Services;

namespace LiveCue.App;

/// <summary>
/// Borderless black window kept glued to one physical monitor while it's enabled - the "ready for
/// the show" backdrop a video/image cue's own window layers on top of (same "newer Topmost window
/// wins" trick those already use to stack on each other), and that reappears on its own once a
/// cue covering it closes, instead of revealing the Windows desktop underneath.
/// </summary>
public partial class MonitorBackgroundWindow : Window
{
    private ScreenBounds _bounds;

    public MonitorBackgroundWindow(ScreenBounds bounds)
    {
        InitializeComponent();
        _bounds = bounds;

        // Rough initial placement only (WPF units); the exact pixel rect is applied once the
        // native window exists, and again after WPF finishes its own initial layout.
        Left = bounds.X;
        Top = bounds.Y;
        Width = bounds.Width;
        Height = bounds.Height;

        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            Win32WindowPlacement.SetNoActivate(hwnd);
            Win32WindowPlacement.ApplyPixelBounds(hwnd, _bounds);
        };
        Loaded += (_, _) => Win32WindowPlacement.ApplyPixelBounds(new WindowInteropHelper(this).Handle, _bounds);
    }

    /// <summary>Repositions/resizes to follow the monitor if its bounds changed (resolution change, moved, ...).</summary>
    public void MoveTo(ScreenBounds bounds)
    {
        _bounds = bounds;
        Win32WindowPlacement.ApplyPixelBounds(new WindowInteropHelper(this).Handle, _bounds);
    }
}
