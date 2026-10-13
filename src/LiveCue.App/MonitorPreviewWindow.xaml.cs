using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using LiveCue.Core.Models;
using LiveCue.Core.Services;

namespace LiveCue.App;

/// <summary>
/// Resizable, movable overview window: draws every active monitor (physical and virtual) to
/// scale, outlined in red, and mirrors what's actually playing on each one live via the DWM
/// thumbnail API — the same mechanism Alt+Tab uses, so it's a real, low-overhead GPU-composited
/// mirror of the cue's own output window, not a periodic screenshot.
/// </summary>
public partial class MonitorPreviewWindow : Window
{
    private const double CanvasPadding = 16;

    private readonly MainViewModel _viewModel;
    private readonly Dictionary<Guid, (IntPtr Thumbnail, ScreenBounds Bounds)> _activeThumbnails = new();

    private double _scale = 1;
    private double _worldOffsetX;
    private double _worldOffsetY;

    public MonitorPreviewWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;

        _viewModel.Displays.CollectionChanged += (_, _) => RedrawStaticLayout();
        _viewModel.VirtualDisplays.CollectionChanged += (_, _) => RedrawStaticLayout();
        _viewModel.Player.VideoWindowOpened += OnVideoWindowOpened;
        _viewModel.Player.VideoWindowClosed += OnVideoWindowClosed;

        Loaded += (_, _) => RedrawStaticLayout();
        Closed += (_, _) => Cleanup();
    }

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e) => RedrawStaticLayout();

    private void PreviewCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => RedrawStaticLayout();

    private void OnVideoWindowOpened(Guid cueId, VideoOutputWindow window)
    {
        Dispatcher.Invoke(() =>
        {
            var destHandle = new WindowInteropHelper(this).Handle;
            var srcHandle = window.Handle;
            if (destHandle == IntPtr.Zero || srcHandle == IntPtr.Zero) return;

            var thumbnail = DwmThumbnail.Register(destHandle, srcHandle);
            _activeThumbnails[cueId] = (thumbnail, window.Bounds);
            UpdateThumbnailRect(cueId);
        });
    }

    private void OnVideoWindowClosed(Guid cueId)
    {
        Dispatcher.Invoke(() =>
        {
            if (!_activeThumbnails.TryGetValue(cueId, out var entry)) return;
            DwmThumbnail.Unregister(entry.Thumbnail);
            _activeThumbnails.Remove(cueId);
        });
    }

    /// <summary>Redraws the red monitor/virtual-display outlines to fit the current window size, and repositions any live thumbnails to match.</summary>
    private void RedrawStaticLayout()
    {
        if (!IsLoaded) return;

        RecomputeScale();

        PreviewCanvas.Children.Clear();

        foreach (var display in _viewModel.Displays)
            DrawOutline(display.X, display.Y, display.Width, display.Height, display.Label, dashed: false);

        foreach (var virtualDisplay in _viewModel.VirtualDisplays)
        {
            var host = _viewModel.Displays.FirstOrDefault(d => d.DeviceName == virtualDisplay.HostDeviceName);
            if (host is null) continue;

            DrawOutline(host.X + virtualDisplay.OffsetX, host.Y + virtualDisplay.OffsetY,
                virtualDisplay.Width, virtualDisplay.Height, virtualDisplay.Label, dashed: true);
        }

        foreach (var cueId in _activeThumbnails.Keys.ToList())
            UpdateThumbnailRect(cueId);
    }

    private void DrawOutline(int worldX, int worldY, int worldWidth, int worldHeight, string label, bool dashed)
    {
        var topLeft = ToCanvas(worldX, worldY);

        var rect = new Rectangle
        {
            Width = Math.Max(1, worldWidth * _scale),
            Height = Math.Max(1, worldHeight * _scale),
            Stroke = Brushes.Red,
            StrokeThickness = 2,
            Fill = Brushes.Black,
        };
        if (dashed) rect.StrokeDashArray = new DoubleCollection { 4, 3 };
        Canvas.SetLeft(rect, topLeft.X);
        Canvas.SetTop(rect, topLeft.Y);
        PreviewCanvas.Children.Add(rect);

        var text = new TextBlock
        {
            Text = label,
            Foreground = Brushes.Red,
            FontSize = 11,
            Padding = new Thickness(3, 1, 3, 1),
            Background = new SolidColorBrush(Color.FromArgb(160, 0, 0, 0)),
        };
        Canvas.SetLeft(text, topLeft.X + 4);
        Canvas.SetTop(text, topLeft.Y + 4);
        PreviewCanvas.Children.Add(text);
    }

    private void UpdateThumbnailRect(Guid cueId)
    {
        if (!_activeThumbnails.TryGetValue(cueId, out var entry)) return;
        if (PresentationSource.FromVisual(this) is null) return; // window not fully initialized yet

        var topLeft = ToCanvas(entry.Bounds.X, entry.Bounds.Y);
        var bottomRight = ToCanvas(entry.Bounds.X + entry.Bounds.Width, entry.Bounds.Y + entry.Bounds.Height);

        var topLeftInWindow = PreviewCanvas.TranslatePoint(topLeft, this);
        var bottomRightInWindow = PreviewCanvas.TranslatePoint(bottomRight, this);

        var dpi = VisualTreeHelper.GetDpi(this);
        var destinationPx = new Int32Rect(
            (int)(topLeftInWindow.X * dpi.DpiScaleX),
            (int)(topLeftInWindow.Y * dpi.DpiScaleY),
            (int)Math.Max(1, (bottomRightInWindow.X - topLeftInWindow.X) * dpi.DpiScaleX),
            (int)Math.Max(1, (bottomRightInWindow.Y - topLeftInWindow.Y) * dpi.DpiScaleY));

        DwmThumbnail.SetRect(entry.Thumbnail, destinationPx);
    }

    private void RecomputeScale()
    {
        if (_viewModel.Displays.Count == 0)
        {
            _scale = 1;
            _worldOffsetX = 0;
            _worldOffsetY = 0;
            return;
        }

        var minX = _viewModel.Displays.Min(d => d.X);
        var minY = _viewModel.Displays.Min(d => d.Y);
        var maxX = _viewModel.Displays.Max(d => d.X + d.Width);
        var maxY = _viewModel.Displays.Max(d => d.Y + d.Height);

        var worldWidth = Math.Max(1, maxX - minX);
        var worldHeight = Math.Max(1, maxY - minY);

        var availableWidth = Math.Max(10, PreviewCanvas.ActualWidth - CanvasPadding * 2);
        var availableHeight = Math.Max(10, PreviewCanvas.ActualHeight - CanvasPadding * 2);

        _scale = Math.Min(availableWidth / worldWidth, availableHeight / worldHeight);
        _worldOffsetX = minX;
        _worldOffsetY = minY;
    }

    private Point ToCanvas(double worldX, double worldY) =>
        new((worldX - _worldOffsetX) * _scale + CanvasPadding, (worldY - _worldOffsetY) * _scale + CanvasPadding);

    private void Cleanup()
    {
        _viewModel.Player.VideoWindowOpened -= OnVideoWindowOpened;
        _viewModel.Player.VideoWindowClosed -= OnVideoWindowClosed;

        foreach (var entry in _activeThumbnails.Values)
            DwmThumbnail.Unregister(entry.Thumbnail);
        _activeThumbnails.Clear();
    }
}
