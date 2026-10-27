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
    public Task GoAsync()
    {
        // A disarmed selected cue is skipped (GO fires the next armed one) instead of making
        // GO silently do nothing and leaving the operator stuck on it.
        var cue = FirstArmedFrom(Math.Max(0, _cueList.SelectedIndex));
        return cue is null ? Task.CompletedTask : FireAsync(cue);
    }

    /// <summary>
    /// Fires a specific cue directly - e.g. a remote-control button (Stream Deck/Companion) that
    /// targets one exact cue - regardless of what's currently selected in the grid. A disarmed
    /// cue is silently skipped, same as GO landing on one.
    /// </summary>
    public Task FireCueAsync(Cue cue) => cue.IsArmed ? FireAsync(cue) : Task.CompletedTask;

    private async Task FireAsync(Cue cue)
    {
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
        // The next cue to chain into is always looked up by list position, never by re-reading
        // CueList.SelectedCue: if this is the last cue, there IS no next one, and SelectedCue
        // would otherwise still point at this same cue — chaining into "itself" forever.
        // Selection moves on BEFORE the pre-wait, so a second GO pressed during the pre-wait
        // fires the next cue instead of firing this one a second time.
        var next = GetNextCue(cue);
        if (next is not null)
            _cueList.SelectedIndex = _cueList.Cues.IndexOf(next);

        if (cue.PreWait > TimeSpan.Zero)
            await Task.Delay(cue.PreWait, ct);

        var playTask = _player.PlayAsync(cue, ct);

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

    /// <summary>The next ARMED cue after this one: disarmed cues are skipped as if not in the list.</summary>
    private Cue? GetNextCue(Cue cue)
    {
        var idx = _cueList.Cues.IndexOf(cue);
        return idx < 0 ? null : FirstArmedFrom(idx + 1);
    }

    private Cue? FirstArmedFrom(int index)
    {
        for (var i = Math.Max(0, index); i < _cueList.Cues.Count; i++)
        {
            if (_cueList.Cues[i].IsArmed) return _cueList.Cues[i];
        }
        return null;
    }
}
