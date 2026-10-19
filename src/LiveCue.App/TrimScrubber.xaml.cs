using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace LiveCue.App;

/// <summary>
/// QLab-style mark in/out scrubber for the inspector's In/Out tab: video thumbnails and/or a
/// waveform across the full file duration, with two draggable handles (Thumb) for TrimStart/
/// TrimEnd. Positions are computed in code-behind (proportional to Duration) rather than in XAML
/// bindings/converters, since a handle's pixel position depends on both a TimeSpan value AND the
/// control's current pixel width at the same time.
/// </summary>
public partial class TrimScrubber : UserControl
{
    public static readonly DependencyProperty DurationProperty = DependencyProperty.Register(
        nameof(Duration), typeof(TimeSpan?), typeof(TrimScrubber), new PropertyMetadata(null, OnVisualPropertyChanged));
    public static readonly DependencyProperty TrimStartProperty = DependencyProperty.Register(
        nameof(TrimStart), typeof(TimeSpan?), typeof(TrimScrubber),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnVisualPropertyChanged));
    public static readonly DependencyProperty TrimEndProperty = DependencyProperty.Register(
        nameof(TrimEnd), typeof(TimeSpan?), typeof(TrimScrubber),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnVisualPropertyChanged));
    public static readonly DependencyProperty WaveformPeaksProperty = DependencyProperty.Register(
        nameof(WaveformPeaks), typeof(float[]), typeof(TrimScrubber), new PropertyMetadata(null, OnVisualPropertyChanged));
    public static readonly DependencyProperty ThumbnailPathsProperty = DependencyProperty.Register(
        nameof(ThumbnailPaths), typeof(IReadOnlyList<string>), typeof(TrimScrubber), new PropertyMetadata(null));
    public static readonly DependencyProperty IsAnalyzingProperty = DependencyProperty.Register(
        nameof(IsAnalyzing), typeof(bool), typeof(TrimScrubber), new PropertyMetadata(false));

    public TimeSpan? Duration { get => (TimeSpan?)GetValue(DurationProperty); set => SetValue(DurationProperty, value); }
    public TimeSpan? TrimStart { get => (TimeSpan?)GetValue(TrimStartProperty); set => SetValue(TrimStartProperty, value); }
    public TimeSpan? TrimEnd { get => (TimeSpan?)GetValue(TrimEndProperty); set => SetValue(TrimEndProperty, value); }
    public float[]? WaveformPeaks { get => (float[]?)GetValue(WaveformPeaksProperty); set => SetValue(WaveformPeaksProperty, value); }
    public IReadOnlyList<string>? ThumbnailPaths { get => (IReadOnlyList<string>?)GetValue(ThumbnailPathsProperty); set => SetValue(ThumbnailPathsProperty, value); }
    public bool IsAnalyzing { get => (bool)GetValue(IsAnalyzingProperty); set => SetValue(IsAnalyzingProperty, value); }

    public TrimScrubber()
    {
        InitializeComponent();
        Loaded += (_, _) => UpdateVisuals();
    }

    private static void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((TrimScrubber)d).UpdateVisuals();

    private void RootGrid_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateVisuals();

    private void UpdateVisuals()
    {
        var width = OverlayCanvas.ActualWidth;
        var height = OverlayCanvas.ActualHeight;
        if (width <= 0 || height <= 0) return;

        if (WaveformPeaks is { Length: > 0 } peaks)
        {
            var midY = height / 2;
            var points = new PointCollection();
            for (var i = 0; i < peaks.Length; i++)
                points.Add(new Point(width * i / peaks.Length, midY - Math.Clamp(peaks[i], 0, 1) * midY));
            for (var i = peaks.Length - 1; i >= 0; i--)
                points.Add(new Point(width * i / peaks.Length, midY + Math.Clamp(peaks[i], 0, 1) * midY));
            WaveformShape.Points = points;
        }
        else
        {
            WaveformShape.Points = new PointCollection();
        }

        if (Duration is not { } duration || duration <= TimeSpan.Zero)
        {
            BeforeStartOverlay.Width = 0;
            AfterEndOverlay.Width = 0;
            StartThumb.Visibility = EndThumb.Visibility = Visibility.Collapsed;
            return;
        }
        StartThumb.Visibility = EndThumb.Visibility = Visibility.Visible;

        var startX = TrimStart is { } s ? width * s.TotalSeconds / duration.TotalSeconds : 0;
        var endX = TrimEnd is { } en ? width * en.TotalSeconds / duration.TotalSeconds : width;

        Canvas.SetLeft(BeforeStartOverlay, 0);
        BeforeStartOverlay.Width = Math.Max(0, startX);
        BeforeStartOverlay.Height = height;

        Canvas.SetLeft(AfterEndOverlay, endX);
        AfterEndOverlay.Width = Math.Max(0, width - endX);
        AfterEndOverlay.Height = height;

        Canvas.SetLeft(StartThumb, startX - StartThumb.Width / 2);
        StartThumb.Height = height;
        Canvas.SetLeft(EndThumb, endX - EndThumb.Width / 2);
        EndThumb.Height = height;
    }

    /// <summary>Snap-to-null distance (seconds): close enough to the clip's own start/end that
    /// "no mark in/out" is clearly what's meant, instead of leaving a near-zero trim forever.</summary>
    private const double SnapSeconds = 0.05;

    private void StartThumb_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (Duration is not { } duration || duration <= TimeSpan.Zero) return;
        var width = OverlayCanvas.ActualWidth;
        if (width <= 0) return;

        var currentX = TrimStart is { } s ? width * s.TotalSeconds / duration.TotalSeconds : 0;
        var endX = TrimEnd is { } en ? width * en.TotalSeconds / duration.TotalSeconds : width;
        var newX = Math.Clamp(currentX + e.HorizontalChange, 0, Math.Max(0, endX - StartThumb.Width));
        var newTime = TimeSpan.FromSeconds(duration.TotalSeconds * newX / width);
        TrimStart = newTime.TotalSeconds < SnapSeconds ? null : newTime;
    }

    private void EndThumb_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (Duration is not { } duration || duration <= TimeSpan.Zero) return;
        var width = OverlayCanvas.ActualWidth;
        if (width <= 0) return;

        var currentX = TrimEnd is { } en ? width * en.TotalSeconds / duration.TotalSeconds : width;
        var startX = TrimStart is { } s ? width * s.TotalSeconds / duration.TotalSeconds : 0;
        var newX = Math.Clamp(currentX + e.HorizontalChange, Math.Min(width, startX + EndThumb.Width), width);
        var newTime = TimeSpan.FromSeconds(duration.TotalSeconds * newX / width);
        TrimEnd = duration.TotalSeconds - newTime.TotalSeconds < SnapSeconds ? null : newTime;
    }
}
