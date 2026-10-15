using LiveCue.Core.Models;
using LiveCue.Core.Services;

namespace LiveCue.Core.Tests;

/// <summary>Records which cues were played; each play "finishes" only when the test says so.</summary>
internal class FakeCuePlayer : ICuePlayer
{
    private readonly Dictionary<Cue, TaskCompletionSource> _running = new();

    public List<Cue> Played { get; } = new();
    public bool StopAllCalled { get; private set; }

    public Task PlayAsync(Cue cue, CancellationToken ct)
    {
        Played.Add(cue);
        var tcs = new TaskCompletionSource();
        ct.Register(() => tcs.TrySetResult());
        _running[cue] = tcs;
        return tcs.Task;
    }

    public void Finish(Cue cue) => _running[cue].TrySetResult();

    public void Stop(Cue cue) => Finish(cue);

    public void StopAll()
    {
        StopAllCalled = true;
        foreach (var tcs in _running.Values) tcs.TrySetResult();
    }
}
