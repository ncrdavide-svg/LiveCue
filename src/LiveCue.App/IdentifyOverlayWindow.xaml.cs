using System.Windows;
using System.Windows.Interop;
using LiveCue.Core.Services;

namespace LiveCue.App;

/// <summary>
/// Borderless overlay that shows one monitor's "Monitor N" number full-size on top of it, like
/// Windows' own Display Settings "Identifica" button — except using LiveCue's own numbering,
/// which is what actually matters: it's what the "Schermo" picker shows, and it isn't guaranteed
/// to match the number Windows' Settings happens to show for the same physical screen (the two
/// come from different, independently-numbered Windows APIs and can disagree after displays are
/// rearranged or reconnected).
/// </summary>
public partial class IdentifyOverlayWindow : Window
{
    public IdentifyOverlayWindow(ScreenBounds bounds, int number)
    {
        InitializeComponent();
        NumberText.Text = number.ToString();

        Left = bounds.X;
        Top = bounds.Y;
        Width = bounds.Width;
        Height = bounds.Height;

        // Two calls, like VideoOutputWindow: the one at SourceInitialized isn't enough on its
        // own on a non-100%-scaled monitor — WPF's own initial layout pass (after Loaded) resets
        // the native size back to its own DPI-scaled guess, undoing it.
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            Win32WindowPlacement.SetNoActivate(hwnd);
            Win32WindowPlacement.ApplyPixelBounds(hwnd, bounds);
        };
        Loaded += (_, _) => Win32WindowPlacement.ApplyPixelBounds(new WindowInteropHelper(this).Handle, bounds);
    }
}
