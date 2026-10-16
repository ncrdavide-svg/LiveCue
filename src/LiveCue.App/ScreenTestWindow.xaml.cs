using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Shapes;
using LiveCue.Core.Services;

namespace LiveCue.App;

/// <summary>
/// Full-screen test card for one monitor: resolution, monitor name, the app logo, and the usual
/// squares/circles/colour-bars pattern used to check geometry, alignment and overscan — the same
/// idea as QLab's screen test. Click anywhere (or Escape) to close.
/// </summary>
public partial class ScreenTestWindow : Window
{
    private const int GridColumns = 16;
    private const int GridRows = 9;

    private readonly ScreenBounds _bounds;

    public ScreenTestWindow(ScreenBounds bounds, string monitorName)
    {
        InitializeComponent();
        _bounds = bounds;

        MonitorNameText.Text = monitorName;
        ResolutionText.Text = $"{bounds.Width} x {bounds.Height}";

        BuildGridLines();

        Left = bounds.X;
        Top = bounds.Y;
        Width = bounds.Width;
        Height = bounds.Height;

        // Two calls, like VideoOutputWindow/IdentifyOverlayWindow: WPF's own initial layout pass
        // (after Loaded) resets the native size back to its DPI-scaled guess on a non-100%-scaled
        // monitor, so the pixel-exact SetWindowPos has to be re-applied there too.
        SourceInitialized += (_, _) => ApplyBounds();
        Loaded += (_, _) =>
        {
            ApplyBounds();
            LayoutOverlay();
        };
        SizeChanged += (_, _) => LayoutOverlay();
    }

    private void ApplyBounds() => Win32WindowPlacement.ApplyPixelBounds(new WindowInteropHelper(this).Handle, _bounds);

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => Close();

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Close();
    }

    private void BuildGridLines()
    {
        GridLines.Rows = GridRows;
        GridLines.Columns = GridColumns;
        for (var i = 0; i < GridRows * GridColumns; i++)
        {
            GridLines.Children.Add(new Border
            {
                BorderBrush = System.Windows.Media.Brushes.White,
                BorderThickness = new Thickness(0.5),
            });
        }
    }

    /// <summary>Sizes the circles and positions the diagonals/crosshair to match the window's actual rendered size.</summary>
    private void LayoutOverlay()
    {
        var w = RootGrid.ActualWidth;
        var h = RootGrid.ActualHeight;
        if (w <= 0 || h <= 0) return;

        var shortSide = Math.Min(w, h);
        SizeCircle(CircleOuter, shortSide * 0.9);
        SizeCircle(CircleMiddle, shortSide * 0.6);
        SizeCircle(CircleInner, shortSide * 0.3);

        PositionLine(DiagonalA, 0, 0, w, h);
        PositionLine(DiagonalB, w, 0, 0, h);
        PositionLine(CrosshairH, 0, h / 2, w, h / 2);
        PositionLine(CrosshairV, w / 2, 0, w / 2, h);
    }

    private static void SizeCircle(Ellipse ellipse, double diameter)
    {
        ellipse.Width = diameter;
        ellipse.Height = diameter;
    }

    private static void PositionLine(Line line, double x1, double y1, double x2, double y2)
    {
        line.X1 = x1;
        line.Y1 = y1;
        line.X2 = x2;
        line.Y2 = y2;
    }
}
