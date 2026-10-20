using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
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

    /// <summary>Win32 window handle, valid once the window has been shown.</summary>
    public IntPtr Handle => new WindowInteropHelper(this).Handle;

    /// <summary>Applies the inspector's pan/zoom/rotate (ImageCue.Zoom/OffsetX/OffsetY/Rotation) live.</summary>
    public void ApplyTransform(double zoom, double offsetX, double offsetY, double rotationDegrees)
    {
        ZoomTransform.ScaleX = zoom;
        ZoomTransform.ScaleY = zoom;
        RotateTransformPart.Angle = rotationDegrees;
        PanTransform.X = offsetX;
        PanTransform.Y = offsetY;
    }

    /// <param name="isTransparentOverlay">
    /// True for a VirtualDisplay.IsTransparentOverlay target: no painted background, so a PNG's
    /// own alpha channel shows whatever is behind this window (desktop, or another cue's output
    /// window on the same monitor - see OverlayZOrderTracker for keeping this one above those).
    /// </param>
    public ImageOutputWindow(ScreenBounds bounds, string filePath, bool isTransparentOverlay = false)
    {
        InitializeComponent();
        Bounds = bounds;

        var background = isTransparentOverlay ? Brushes.Transparent : Brushes.Black;
        Background = background;
        RootGrid.Background = background;

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
