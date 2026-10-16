using System.Windows;
using System.Windows.Interop;
using LiveCue.Core.Services;

namespace LiveCue.App;

/// <summary>
/// A plain black overlay placed exactly over a video cue's output window and faded in
/// (Opacity 0 to 1) to visually fade that video to black. Animating the video window's own
/// Opacity instead would blend it with whatever is behind it on the desktop, not black - this
/// window stays fully opaque as a window and a layered black window on top does the actual
/// fading, the same "new Topmost window layers above the older one" trick VideoOutputWindow
/// already relies on for stacking multiple video cues on the same screen.
/// </summary>
public partial class FadeOverlayWindow : Window
{
    public FadeOverlayWindow(ScreenBounds bounds)
    {
        InitializeComponent();

        Left = bounds.X;
        Top = bounds.Y;
        Width = bounds.Width;
        Height = bounds.Height;

        // Two calls, like VideoOutputWindow/IdentifyOverlayWindow: WPF's own initial layout pass
        // (after Loaded) resets the native size back to its DPI-scaled guess on a non-100%-scaled
        // monitor, so the pixel-exact SetWindowPos has to be re-applied there too.
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            Win32WindowPlacement.SetNoActivate(hwnd);
            Win32WindowPlacement.ApplyPixelBounds(hwnd, bounds);
        };
        Loaded += (_, _) => Win32WindowPlacement.ApplyPixelBounds(new WindowInteropHelper(this).Handle, bounds);
    }
}
