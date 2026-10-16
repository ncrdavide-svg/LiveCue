using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using LiveCue.Core.Services;

namespace LiveCue.App;

/// <summary>
/// Borderless, always-on-top window showing one still image on a target rectangle - the same
/// positioning/no-activate machinery as VideoOutputWindow, with a WPF Image instead of LibVLC's
/// video surface (an image cue has no MediaPlayer at all).
/// </summary>
public partial class ImageOutputWindow : Window
{
    public ScreenBounds Bounds { get; }

    public ImageOutputWindow(ScreenBounds bounds, string filePath)
    {
        InitializeComponent();
        Bounds = bounds;

        // OnLoad + Freeze: reads the whole file up front and releases the handle immediately, so
        // the source file can be replaced/deleted without affecting what's already on screen, and
        // the frozen BitmapImage needs no further thread affinity/locking to display.
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.UriSource = new Uri(filePath, UriKind.Absolute);
        image.EndInit();
        image.Freeze();
        ImageControl.Source = image;

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
            Win32WindowPlacement.ApplyPixelBounds(hwnd, bounds);
        };
        Loaded += (_, _) => Win32WindowPlacement.ApplyPixelBounds(new WindowInteropHelper(this).Handle, bounds);
    }
}
