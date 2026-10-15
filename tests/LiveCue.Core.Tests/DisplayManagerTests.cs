using LiveCue.Core.Models;
using LiveCue.Core.Services;

namespace LiveCue.Core.Tests;

public class DisplayManagerTests
{
    private static readonly DisplayInfo Primary = new(@"\\.\DISPLAY1", "A", 1, 0, 0, 2560, 1440, true);
    private static readonly DisplayInfo Secondary = new(@"\\.\DISPLAY2", "B", 2, 2560, 0, 1920, 1080, false);

    [Fact]
    public void ResolvesPhysicalDisplay()
    {
        var bounds = DisplayManager.ResolveBounds(Secondary.Id, new[] { Primary, Secondary }, Array.Empty<VirtualDisplay>());
        Assert.Equal(new ScreenBounds(2560, 0, 1920, 1080), bounds);
    }

    [Fact]
    public void ResolvesVirtualDisplay_RelativeToItsHost()
    {
        var virtualDisplay = new VirtualDisplay { HostDeviceName = Secondary.DeviceName, OffsetX = 100, OffsetY = 50, Width = 1280, Height = 720 };
        var bounds = DisplayManager.ResolveBounds(virtualDisplay.Id, new[] { Primary, Secondary }, new[] { virtualDisplay });
        Assert.Equal(new ScreenBounds(2660, 50, 1280, 720), bounds);
    }

    [Fact]
    public void OutputBounds_MissingTarget_FallsBackToSecondary_WithWarning()
    {
        var bounds = DisplayManager.ResolveOutputBounds(@"\\.\DISPLAY9", new[] { Primary, Secondary }, Array.Empty<VirtualDisplay>(), out var warning);
        Assert.Equal(new ScreenBounds(2560, 0, 1920, 1080), bounds);
        Assert.NotNull(warning);
    }

    [Fact]
    public void OutputBounds_MissingTarget_NeverUsesOperatorScreen()
    {
        var bounds = DisplayManager.ResolveOutputBounds(@"\\.\DISPLAY2", new[] { Primary }, Array.Empty<VirtualDisplay>(), out var warning);
        Assert.Null(bounds);
        Assert.NotNull(warning);
    }

    [Fact]
    public void OutputBounds_VirtualDisplayWithUnpluggedHost_NeverUsesOperatorScreen()
    {
        var virtualDisplay = new VirtualDisplay { HostDeviceName = Secondary.DeviceName };
        var bounds = DisplayManager.ResolveOutputBounds(virtualDisplay.Id, new[] { Primary }, new[] { virtualDisplay }, out _);
        Assert.Null(bounds);
    }

    [Fact]
    public void OutputBounds_NoTarget_UsesPrimary()
    {
        var bounds = DisplayManager.ResolveOutputBounds(null, new[] { Primary, Secondary }, Array.Empty<VirtualDisplay>(), out var warning);
        Assert.Equal(new ScreenBounds(0, 0, 2560, 1440), bounds);
        Assert.Null(warning);
    }

    [Fact]
    public void UnknownTarget_ReturnsNull()
    {
        Assert.Null(DisplayManager.ResolveBounds("nope", new[] { Primary }, Array.Empty<VirtualDisplay>()));
    }
}
