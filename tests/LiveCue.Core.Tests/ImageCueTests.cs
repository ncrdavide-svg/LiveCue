using LiveCue.Core.Models;
using Xunit;

namespace LiveCue.Core.Tests;

public class ImageCueTests
{
    [Fact]
    public void Duration_IsNull_WhenDisplaySecondsIsZero()
    {
        var cue = new ImageCue();

        Assert.Null(cue.Duration);
    }

    [Fact]
    public void Duration_MirrorsDisplaySeconds_WhenPositive()
    {
        var cue = new ImageCue { DisplaySeconds = 4.5 };

        Assert.Equal(TimeSpan.FromSeconds(4.5), cue.Duration);
    }

    [Fact]
    public void Duration_GoesBackToNull_WhenDisplaySecondsResetToZero()
    {
        var cue = new ImageCue { DisplaySeconds = 4.5 };

        cue.DisplaySeconds = 0;

        Assert.Null(cue.Duration);
    }

    [Fact]
    public void Type_IsImage()
    {
        Assert.Equal(CueType.Image, new ImageCue().Type);
    }
}
