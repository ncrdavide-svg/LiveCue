using LiveCue.Core.Models;

namespace LiveCue.Core.Services;

/// <summary>
/// Abstraction over actual media playback so LiveCue.Core stays free of any
/// UI/media-framework dependency (the WPF app implements this using LibVLCSharp).
/// </summary>
public interface ICuePlayer
{
    /// <summary>
    /// Starts playback and completes when the cue reaches its end for the first time, or is
    /// stopped. A looping cue keeps playing after the returned task completes (until stopped),
    /// so a "Continua" cue that loops still hands off to the next cue after one pass.
    /// </summary>
    Task PlayAsync(Cue cue, CancellationToken ct);

    void Stop(Cue cue);

    void StopAll();
}
