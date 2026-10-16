using LiveCue.Core.Models;
using Xunit;

namespace LiveCue.Core.Tests;

public class StopCueTests
{
    [Fact]
    public void Type_IsStop()
    {
        Assert.Equal(CueType.Stop, new StopCue().Type);
    }

    [Fact]
    public void TargetCueNumber_DefaultsToEmpty()
    {
        Assert.Equal(string.Empty, new StopCue().TargetCueNumber);
    }
}
