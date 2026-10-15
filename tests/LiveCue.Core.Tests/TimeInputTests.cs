using LiveCue.Core.Services;

namespace LiveCue.Core.Tests;

public class TimeInputTests
{
    [Theory]
    [InlineData("5", 5)]
    [InlineData("2.5", 2.5)]
    [InlineData("2,5", 2.5)]
    [InlineData(" 10 ", 10)]
    [InlineData("1:30", 90)]
    [InlineData("1:00:05", 3605)]
    [InlineData("0:02,5", 2.5)]
    [InlineData("", 0)]
    public void ParsesSecondsAndClockFormats(string input, double expectedSeconds)
    {
        Assert.True(TimeInput.TryParse(input, out var value));
        Assert.Equal(expectedSeconds, value.TotalSeconds, precision: 3);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("-5")]
    [InlineData("1:2:3:4")]
    [InlineData("999999")]
    public void RejectsInvalidInput(string input)
    {
        Assert.False(TimeInput.TryParse(input, out _));
    }

    [Fact]
    public void RoundTripsThroughFormat()
    {
        var formatted = TimeInput.Format(TimeSpan.FromSeconds(2.5));
        Assert.True(TimeInput.TryParse(formatted, out var value));
        Assert.Equal(2.5, value.TotalSeconds, precision: 3);
    }
}
