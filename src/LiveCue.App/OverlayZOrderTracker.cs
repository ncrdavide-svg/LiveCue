using System.Windows;
using System.Windows.Threading;

namespace LiveCue.App;

/// <summary>
/// Keeps a transparent overlay window (see VirtualDisplay.IsTransparentOverlay) visually above
/// every other output window on the same physical monitor, regardless of which one opened or
/// closed first. WPF's Topmost="True" only wins HWND_TOPMOST at the moment a window is shown -
/// among several topmost windows, whichever was most recently (re-)asserted topmost is drawn on
/// top, so an older overlay can otherwise end up hidden behind a base video/image opened later
/// on the same screen.
/// </summary>
internal sealed class OverlayZOrderTracker
{
    private readonly Dictionary<string, IntPtr> _overlaysByHost = new();

    /// <summary>Call right after any output window (video or image) is shown.</summary>
    public void WindowOpened(string? hostDeviceName, IntPtr hwnd, bool isOverlay)
    {
        if (hostDeviceName is null) return;

        if (isOverlay)
        {
            _overlaysByHost[hostDeviceName] = hwnd;
            BringToTopDeferred(hwnd);
        }
        else if (_overlaysByHost.TryGetValue(hostDeviceName, out var overlayHwnd) && overlayHwnd != hwnd)
        {
            // A base window just opened on the same monitor as an existing overlay - without
            // this it could end up drawn above the overlay instead of under it.
            BringToTopDeferred(overlayHwnd);
        }
    }

    /// <summary>
    /// A freshly-shown WPF window keeps setting up its own Win32 state (SourceInitialized,
    /// Loaded, DWM frame) on later dispatcher ticks even after Show() has returned - including,
    /// for VideoOutputWindow/ImageOutputWindow, their own ApplyPixelBounds/topmost calls. Calling
    /// SetWindowPos for the overlay synchronously, right here, can still end up overtaken by that
    /// later setup. Posting at Background priority runs after all of it has settled, so the
    /// overlay reliably ends up on top no matter which window opened first.
    /// </summary>
    private static void BringToTopDeferred(IntPtr hwnd)
        => Application.Current?.Dispatcher.BeginInvoke(DispatcherPriority.Background, () => Win32WindowPlacement.BringToTop(hwnd));

    /// <summary>Call right before an overlay output window closes.</summary>
    public void WindowClosed(string? hostDeviceName, IntPtr hwnd)
    {
        if (hostDeviceName is not null
            && _overlaysByHost.TryGetValue(hostDeviceName, out var overlayHwnd)
            && overlayHwnd == hwnd)
            _overlaysByHost.Remove(hostDeviceName);
    }
}
