using LiveCue.Core.Models;

namespace LiveCue.Core.Services;

/// <summary>
/// Drives the cue list: GO/stop and pre-wait/post-wait/auto-continue/auto-follow sequencing.
/// Actual media playback is delegated to an ICuePlayer so this class has no media dependency.
/// </summary>
public class CueEngine
{
    private readonly CueList _cueList;
    private readonly ICuePlayer _player;
    private readonly List<CancellationTokenSource> _activeRuns = new();

    public CueEngine(CueList cueList, ICuePlayer player)
    {
        _cueList = cueList;
        _player = player;
    }

    public bool IsRunning => _activeRuns.Count > 0;

    /// <summary>
    /// Fires the currently selected cue (or the first cue if none is selected) and advances selection.
    /// Each GO starts its own independent chain: it never stops a cue (or chain) already playing
    /// from an earlier GO, so multiple cues can run together, started at different times. Only
    /// Stop()/Panic() stops everything.
    /// </summary>
    public async Task GoAsync()
    {
        var cue = _cueList.SelectedCue ?? (_cueList.Cues.Count > 0 ? _cueList.Cues[0] : null);
        if (cue is null || !cue.IsArmed) return;

        var cts = new CancellationTokenSource();
        _activeRuns.Add(cts);
        try
        {
            await ExecuteCueAsync(cue, cts.Token);
        }
        catch (OperationCanceledException)
        {
            // expected on Stop()/Panic()
        }
        finally
        {
            _activeRuns.Remove(cts);
            cts.Dispose();
        }
    }

    public void Stop()
    {
        foreach (var cts in _activeRuns.ToList())
            cts.Cancel();
        _player.StopAll();
    }

    /// <summary>Immediately stops everything (the "panic" button).</summary>
    public void Panic() => Stop();

    private async Task ExecuteCueAsync(Cue cue, CancellationToken ct)
    {
        if (cue.PreWait > TimeSpan.Zero)
            await Task.Delay(cue.PreWait, ct);

        var playTask = _player.PlayAsync(cue, ct);

        // The next cue to chain into is always looked up by list position, never by re-reading
        // CueList.SelectedCue: if this is the last cue, there IS no next one, and SelectedCue
        // would otherwise still point at this same cue — chaining into "itself" forever.
        var next = GetNextCue(cue);
        if (next is not null)
            _cueList.SelectedIndex = _cueList.Cues.IndexOf(next);

        switch (cue.ContinueMode)
        {
            case ContinueMode.StartTogether:
                // Move to the next cue immediately, without waiting for this one to finish playing.
                if (cue.PostWait > TimeSpan.Zero)
                    await Task.Delay(cue.PostWait, ct);
                if (next is not null)
                    await ExecuteCueAsync(next, ct);
                break;

            case ContinueMode.Continue:
                // Wait for this cue to finish playing, then continue to the next.
                await playTask;
                if (cue.PostWait > TimeSpan.Zero)
                    await Task.Delay(cue.PostWait, ct);
                if (next is not null)
                    await ExecuteCueAsync(next, ct);
                break;

            case ContinueMode.DoNotContinue:
            default:
                break;
        }
    }

    private Cue? GetNextCue(Cue cue)
    {
        var idx = _cueList.Cues.IndexOf(cue);
        return idx >= 0 && idx + 1 < _cueList.Cues.Count ? _cueList.Cues[idx + 1] : null;
    }
}
