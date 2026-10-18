using System.IO;

namespace LiveCue.Core.Models;

/// <summary>
/// Shows a still image full-screen on a target display. Unlike video/audio there's no natural
/// end: with DisplaySeconds at 0 it just stays up until stopped, PANIC, or another cue takes over
/// the same screen; set above 0 and it dismisses itself automatically after that long, like a
/// timed slide, so a "Continua" chain can move past it on its own.
/// Deliberately not a MediaCue: Volume/Loop don't mean anything for a still picture.
/// </summary>
public class ImageCue : Cue, IFileCue
{
    public override CueType Type => CueType.Image;

    private string _filePath = string.Empty;
    public string FilePath
    {
        get => _filePath;
        set
        {
            SetField(ref _filePath, value);
            FileMissing = _filePath.Length > 0 && !File.Exists(_filePath);
        }
    }

    /// <summary>Id of the target display (a physical DisplayInfo.Id or a VirtualDisplay.Id) this cue shows on.</summary>
    private string? _targetDisplayId;
    public string? TargetDisplayId { get => _targetDisplayId; set => SetField(ref _targetDisplayId, value); }

    private double _displaySeconds;
    /// <summary>0 = stays on screen until stopped. Mirrored into the base Duration field, like
    /// FadeCue.FadeSeconds, so the "Durata" column shows it without a dedicated display column.</summary>
    public double DisplaySeconds
    {
        get => _displaySeconds;
        set
        {
            SetField(ref _displaySeconds, value);
            Duration = value > 0 ? TimeSpan.FromSeconds(value) : null;
        }
    }
}
