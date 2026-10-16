namespace LiveCue.Core.Models;

/// <summary>
/// A cue with no media of its own: fades another cue's audio (and, if it's a video, its picture
/// too) down to nothing over a chosen duration, then stops it. The target is the other cue's
/// Number - the same text already shown in the "#" column - not its Id, so it can be rewired just
/// by editing a text field, same as how QLab's fade cues target another cue.
/// </summary>
public class FadeCue : Cue
{
    public override CueType Type => CueType.Fade;

    private string _targetCueNumber = string.Empty;
    public string TargetCueNumber { get => _targetCueNumber; set => SetField(ref _targetCueNumber, value); }

    private double _fadeSeconds = 2;
    /// <summary>
    /// Mirrored into the base Duration field so the "Durata" column shows it like every other
    /// cue, without needing a dedicated display column.
    /// </summary>
    public double FadeSeconds
    {
        get => _fadeSeconds;
        set
        {
            SetField(ref _fadeSeconds, value);
            Duration = TimeSpan.FromSeconds(Math.Max(0, _fadeSeconds));
        }
    }

    public FadeCue() => Duration = TimeSpan.FromSeconds(_fadeSeconds);
}
