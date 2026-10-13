namespace LiveCue.Core.Models;

public class VideoCue : Cue
{
    public override CueType Type => CueType.Video;

    private string _filePath = string.Empty;
    public string FilePath { get => _filePath; set => SetField(ref _filePath, value); }

    /// <summary>Id of the target display (a physical DisplayInfo.Id or a VirtualDisplay.Id) this cue plays on.</summary>
    private string? _targetDisplayId;
    public string? TargetDisplayId { get => _targetDisplayId; set => SetField(ref _targetDisplayId, value); }

    private double _volume = 1.0;
    public double Volume { get => _volume; set => SetField(ref _volume, value); }

    private bool _loop;
    public bool Loop { get => _loop; set => SetField(ref _loop, value); }

    private bool _fullScreen = true;
    public bool FullScreen { get => _fullScreen; set => SetField(ref _fullScreen, value); }
}
