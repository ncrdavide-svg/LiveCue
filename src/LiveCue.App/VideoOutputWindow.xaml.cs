using System.Runtime.InteropServices;
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
            SetNoActivate();
            ApplyBounds();
        };
        Loaded += (_, _) => ApplyBounds();
    }

    /// <summary>
    /// WPF's ShowActivated/Focusable only govern activation WPF itself triggers — LibVLC's video
    /// surface is a native child window that can call SetForegroundWindow on its own as playback
    /// starts, stealing keyboard focus from the main window regardless of those properties. Was:
    /// if a cue's screen is the one the operator's own window is on, the video (topmost,
    /// fullscreen) would take focus and the operator couldn't click past it OR use the ESC/PANIC
    /// shortcut to get out, since ESC no longer reached the main window at all.
    /// WS_EX_NOACTIVATE is enforced at the Win32 level, so nothing — WPF or native — can activate
    /// this window; the main window always keeps keyboard focus.
    /// </summary>
    private void SetNoActivate()
    {
        var exStyle = GetWindowLongPtr(Handle, GWL_EXSTYLE);
        SetWindowLongPtr(Handle, GWL_EXSTYLE, new IntPtr(exStyle.ToInt64() | WS_EX_NOACTIVATE));
    }

    public void MoveToDisplay(ScreenBounds bounds)
    {
        Bounds = bounds;
        ApplyBounds();
    }

    public void Attach(MediaPlayer mediaPlayer) => VideoViewControl.MediaPlayer = mediaPlayer;

    public void Detach() => VideoViewControl.MediaPlayer = null;

    private void ApplyBounds()
    {
        var hwnd = Handle;
        if (hwnd == IntPtr.Zero) return;
        SetWindowPos(hwnd, IntPtr.Zero, Bounds.X, Bounds.Y, Bounds.Width, Bounds.Height, SWP_NOZORDER | SWP_NOACTIVATE);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // Moving onto a monitor with a different scale makes WPF resize the window to Windows'
        // "suggested" rect, which keeps the logical size rather than our pixel size. Let WPF
        // update its DPI, then put the window back on our exact pixel rect.
        if (msg == WM_DPICHANGED)
            Dispatcher.BeginInvoke(ApplyBounds);
        return IntPtr.Zero;
    }

    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_NOACTIVATE = 0x08000000;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
}
