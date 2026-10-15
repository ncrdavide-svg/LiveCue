namespace LiveCue.Core.Models;

public class VideoCue : MediaCue
{
    public override CueType Type => CueType.Video;

    /// <summary>Id of the target display (a physical DisplayInfo.Id or a VirtualDisplay.Id) this cue plays on.</summary>
    private string? _targetDisplayId;
    public string? TargetDisplayId { get => _targetDisplayId; set => SetField(ref _targetDisplayId, value); }

    private bool _fullScreen = true;
    public bool FullScreen { get => _fullScreen; set => SetField(ref _fullScreen, value); }
}
