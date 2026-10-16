namespace LiveCue.Core.Models;

/// <summary>Base for cues that play a media file (audio or video).</summary>
public abstract class MediaCue : Cue, IFileCue
{
    private string _filePath = string.Empty;
    public string FilePath { get => _filePath; set => SetField(ref _filePath, value); }

    private double _volume = 1.0;
    public double Volume { get => _volume; set => SetField(ref _volume, value); }

    private bool _loop;
    public bool Loop { get => _loop; set => SetField(ref _loop, value); }
}
