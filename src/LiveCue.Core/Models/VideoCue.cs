using System.Text.Json.Serialization;

namespace LiveCue.Core.Models;

public class VideoCue : MediaCue
{
    public override CueType Type => CueType.Video;

    /// <summary>Id of the target display (a physical DisplayInfo.Id or a VirtualDisplay.Id) this cue plays on.</summary>
    private string? _targetDisplayId;
    public string? TargetDisplayId { get => _targetDisplayId; set => SetField(ref _targetDisplayId, value); }

    private bool _fullScreen = true;
    public bool FullScreen { get => _fullScreen; set => SetField(ref _fullScreen, value); }

    // --- Basic color correction (LibVLC's "adjust" video filter), applied live while playing -
    // see MediaPlayerCuePlayer.PlayMediaAsync/OnCuePropertyChanged. 1.0 is VLC's own neutral
    // default for all three (0.0-2.0 for Brightness/Contrast, 0.0-3.0 for Saturation). ---

    private double _brightness = 1.0;
    public double Brightness { get => _brightness; set => SetField(ref _brightness, value); }

    private double _contrast = 1.0;
    public double Contrast { get => _contrast; set => SetField(ref _contrast, value); }

    private double _saturation = 1.0;
    public double Saturation { get => _saturation; set => SetField(ref _saturation, value); }

    // --- Pan/zoom/rotate, applied as a WPF RenderTransform on the output window's content (see
    // VideoOutputWindow.ApplyTransform) rather than through LibVLC - a plain visual transform
    // works identically for a video or a still image, needs no native filter, and nothing here
    // risks the kind of native crash the waveform's LibVLC audio callbacks did. ---

    private double _zoom = 1.0;
    public double Zoom { get => _zoom; set => SetField(ref _zoom, value); }

    private double _offsetX;
    public double OffsetX { get => _offsetX; set => SetField(ref _offsetX, value); }

    private double _offsetY;
    public double OffsetY { get => _offsetY; set => SetField(ref _offsetY, value); }

    private double _rotation;
    public double Rotation { get => _rotation; set => SetField(ref _rotation, value); }

    // --- Technical info for the inspector's "Informazioni" tab - see MediaCue.Bitrate/Codec. ---

    private uint? _videoWidth;
    [JsonIgnore]
    public uint? VideoWidth { get => _videoWidth; set => SetField(ref _videoWidth, value); }

    private uint? _videoHeight;
    [JsonIgnore]
    public uint? VideoHeight { get => _videoHeight; set => SetField(ref _videoHeight, value); }

    private double? _frameRate;
    [JsonIgnore]
    public double? FrameRate { get => _frameRate; set => SetField(ref _frameRate, value); }

    /// <summary>Evenly-spaced preview frame files for the In/Out tab's scrubber - see
    /// MediaPlayerCuePlayer.AnalyzeMediaAsync. Null until generated. Not persisted (cached on
    /// disk separately, keyed by file content - see MediaAnalysisCache).</summary>
    private IReadOnlyList<string>? _thumbnailPaths;
    [JsonIgnore]
    public IReadOnlyList<string>? ThumbnailPaths { get => _thumbnailPaths; set => SetField(ref _thumbnailPaths, value); }
}
