using System.Windows;
using System.Windows.Interop;
using LibVLCSharp.Shared;
using LiveCue.Core.Services;

namespace LiveCue.App;

/// <summary>
/// Borderless, always-on-top window placed over a target rectangle (a physical display, or a
/// virtual display region on top of one) to show cue video output.
/// <see cref="Bounds"/> is in device pixels (as reported by Win32), while WPF's Left/Top/Width/
/// Height are in DPI-scaled units — so the window is placed with SetWindowPos, in pixels, which
/// stays exact whatever the scaling of the target monitor (100%, 125%, 150%...).
/// </summary>
public partial class VideoOutputWindow : Window
{
    private const int WM_DPICHANGED = 0x02E0;

    public ScreenBounds Bounds { get; private set; }

    /// <summary>Win32 window handle, valid once the window has been shown.</summary>
    public IntPtr Handle => new WindowInteropHelper(this).Handle;

    public VideoOutputWindow(ScreenBounds bounds)
    {
        InitializeComponent();
        Bounds = bounds;
        // Rough initial placement only (WPF units); the exact pixel rect is applied once the
        // native window exists, and again after WPF finishes its own initial layout.
        Left = bounds.X;
        Top = bounds.Y;
        Width = bounds.Width;
        Height = bounds.Height;

        SourceInitialized += (_, _) =>
        {
            HwndSource.FromHwnd(Handle)?.AddHook(WndProc);
            // WPF's ShowActivated/Focusable only govern activation WPF itself triggers — LibVLC's
            // video surface is a native child window that can call SetForegroundWindow on its own
            // as playback starts, stealing keyboard focus from the main window regardless of those
            // properties. Was: if a cue's screen is the one the operator's own window is on, the
            // video (topmost, fullscreen) would take focus and the operator couldn't click past it
            // OR use the ESC/PANIC shortcut to get out, since ESC no longer reached the main window
            // at all. WS_EX_NOACTIVATE is enforced at the Win32 level, so nothing — WPF or native —
            // can activate this window; the main window always keeps keyboard focus.
            Win32WindowPlacement.SetNoActivate(Handle);
            ApplyBounds();
        };
        Loaded += (_, _) => ApplyBounds();
    }

    public void MoveToDisplay(ScreenBounds bounds)
    {
        Bounds = bounds;
        ApplyBounds();
    }

    public void Attach(MediaPlayer mediaPlayer) => VideoViewControl.MediaPlayer = mediaPlayer;

    public void Detach() => VideoViewControl.MediaPlayer = null;

    /// <summary>Applies the inspector's pan/zoom/rotate (VideoCue.Zoom/OffsetX/OffsetY/Rotation) live.</summary>
    public void ApplyTransform(double zoom, double offsetX, double offsetY, double rotationDegrees)
    {
        ZoomTransform.ScaleX = zoom;
        ZoomTransform.ScaleY = zoom;
        RotateTransformPart.Angle = rotationDegrees;
        PanTransform.X = offsetX;
        PanTransform.Y = offsetY;
    }

    private void ApplyBounds() => Win32WindowPlacement.ApplyPixelBounds(Handle, Bounds);

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // Moving onto a monitor with a different scale makes WPF resize the window to Windows'
        // "suggested" rect, which keeps the logical size rather than our pixel size. Let WPF
        // update its DPI, then put the window back on our exact pixel rect.
        if (msg == WM_DPICHANGED)
            Dispatcher.BeginInvoke(ApplyBounds);
        return IntPtr.Zero;
    }
}
