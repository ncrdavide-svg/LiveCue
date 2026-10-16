namespace LiveCue.Core.Models;

/// <summary>
/// A cue with no media of its own: immediately stops another cue (by its Number), no fade. Same
/// targeting idea as FadeCue, just instant instead of ramped.
/// </summary>
public class StopCue : Cue
{
    public override CueType Type => CueType.Stop;

    private string _targetCueNumber = string.Empty;
    public string TargetCueNumber { get => _targetCueNumber; set => SetField(ref _targetCueNumber, value); }
}
