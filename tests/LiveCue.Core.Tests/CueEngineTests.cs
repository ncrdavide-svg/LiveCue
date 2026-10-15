using LiveCue.Core.Models;
using LiveCue.Core.Services;

namespace LiveCue.Core.Tests;

public class CueEngineTests
{
    private static (CueList list, FakeCuePlayer player, CueEngine engine) Create(params Cue[] cues)
    {
        var list = new CueList();
        foreach (var cue in cues) list.Cues.Add(cue);
        list.SelectedIndex = 0;
        var player = new FakeCuePlayer();
        return (list, player, new CueEngine(list, player));
    }

    private static AudioCue Audio(string number, ContinueMode mode = ContinueMode.DoNotContinue) =>
        new() { Number = number, FilePath = $"{number}.wav", ContinueMode = mode };

    [Fact]
    public async Task Go_PlaysSelectedCue_AndAdvancesSelection()
    {
        var (list, player, engine) = Create(Audio("1"), Audio("2"));

        await engine.GoAsync();

        Assert.Equal(new[] { list.Cues[0] }, player.Played);
        Assert.Equal(1, list.SelectedIndex);
    }

    [Fact]
    public async Task StartTogether_FiresNextCueWithoutWaiting()
    {
        var (list, player, engine) = Create(Audio("1", ContinueMode.StartTogether), Audio("2"));

        await engine.GoAsync();

        Assert.Equal(list.Cues, player.Played);
    }

    [Fact]
    public async Task Continue_FiresNextCueOnlyAfterFirstOneFinishes()
    {
        var (list, player, engine) = Create(Audio("1", ContinueMode.Continue), Audio("2"));

        var go = engine.GoAsync();
        Assert.Single(player.Played);

        player.Finish(list.Cues[0]);
        await go;
        Assert.Equal(list.Cues, player.Played);
    }

    [Fact]
    public async Task Go_DuringPreWait_FiresNextCue_NotTheSameOneAgain()
    {
        var first = Audio("1");
        first.PreWait = TimeSpan.FromMilliseconds(200);
        var (list, player, engine) = Create(first, Audio("2"));

        var firstGo = engine.GoAsync();   // still in the pre-wait
        await engine.GoAsync();           // second GO, pressed during the pre-wait
        await firstGo;

        Assert.Equal(2, player.Played.Count);
        Assert.Contains(list.Cues[0], player.Played);
        Assert.Contains(list.Cues[1], player.Played);
    }

    [Fact]
    public void RemovingLastCue_ResetsNewLastCueToDoNotContinue()
    {
        var (list, _, _) = Create(Audio("1", ContinueMode.Continue), Audio("2"));

        list.Cues.RemoveAt(1);

        Assert.Equal(ContinueMode.DoNotContinue, list.Cues[0].ContinueMode);
    }

    [Fact]
    public async Task LastCue_NeverChainsIntoItself()
    {
        var (list, player, engine) = Create(Audio("1"));
        list.Cues[0].ContinueMode = ContinueMode.StartTogether;

        await engine.GoAsync();

        Assert.Single(player.Played);
    }

    [Fact]
    public async Task Stop_CancelsPreWait_AndStopsPlayer()
    {
        var first = Audio("1");
        first.PreWait = TimeSpan.FromSeconds(30);
        var (_, player, engine) = Create(first);

        var go = engine.GoAsync();
        engine.Stop();
        await go;

        Assert.Empty(player.Played);
        Assert.True(player.StopAllCalled);
        Assert.False(engine.IsRunning);
    }
}
