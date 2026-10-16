using LiveCue.Core.Models;
using Xunit;

namespace LiveCue.Core.Tests;

public class FadeCueTests
{
    [Fact]
    public void Duration_MirrorsFadeSeconds_FromConstruction()
    {
        var cue = new FadeCue();

        Assert.Equal(TimeSpan.FromSeconds(2), cue.Duration);
    }

    [Fact]
    public void Duration_UpdatesWhenFadeSecondsChanges()
    {
        var cue = new FadeCue { FadeSeconds = 5.5 };

        Assert.Equal(TimeSpan.FromSeconds(5.5), cue.Duration);
    }

    [Fact]
    public void Type_IsFade()
    {
        Assert.Equal(CueType.Fade, new FadeCue().Type);
    }
}
