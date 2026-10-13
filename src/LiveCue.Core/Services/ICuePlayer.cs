using LiveCue.Core.Models;

namespace LiveCue.Core.Services;

/// <summary>
/// Abstraction over actual media playback so LiveCue.Core stays free of any
/// UI/media-framework dependency (the WPF app implements this using LibVLCSharp).
/// </summary>
public interface ICuePlayer
{
    /// <summary>Starts playback and completes when the cue finishes (or is stopped).</summary>
    Task PlayAsync(Cue cue, CancellationToken ct);

    void Stop(Cue cue);

    void StopAll();
}
