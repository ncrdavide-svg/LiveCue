using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using LiveCue.Core.Models;

namespace LiveCue.Core.Services;

/// <summary>
/// A physical monitor. <see cref="DeviceName"/> (e.g. "\\.\DISPLAY1") is the stable Win32
/// identifier used for API calls; <see cref="MonitorName"/>/<see cref="Label"/> are what
/// should be shown to the user — NOT the graphics card name, which is the same for every
/// monitor plugged into one GPU and tells you nothing about which physical screen it is.
/// </summary>
public record DisplayInfo(string DeviceName, string MonitorName, int DisplayNumber, int X, int Y, int Width, int Height, bool IsPrimary)
{
    /// <summary>Same as DeviceName — lets XAML bind "Id" uniformly across DisplayInfo and VirtualDisplay.</summary>
    public string Id => DeviceName;

    /// <summary>
    /// Operator preference, not an OS fact: whether this monitor should appear in the "pick a
    /// target screen" lists (cue "Schermo" column, virtual-display host picker). Outside the
    /// record's primary constructor so a `with` copy (used for the custom-name override) still
    /// carries it, while GetDisplays()'s own fresh instances default to enabled.
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    public string Label => $"Monitor {DisplayNumber}: {MonitorName}" + (IsPrimary ? " (primario)" : "");

    public override string ToString() => Label;
}

public record DisplayMode(int Width, int Height, int RefreshRate, int BitsPerPixel)
{
    public override string ToString() => $"{Width}x{Height} @ {RefreshRate}Hz ({BitsPerPixel}-bit)";
}

/// <summary>Resolved on-screen pixel rectangle for a cue's playback target (physical or virtual).</summary>
public record ScreenBounds(int X, int Y, int Width, int Height);

/// <summary>
/// A cue's resolved playback target, with the extra context needed for a transparent overlay
/// window: which physical monitor it ultimately lands on (so an overlay knows which other
/// output windows it needs to stay above - see MediaPlayerCuePlayer's Z-order coordination),
/// and whether the target itself is a transparent-overlay VirtualDisplay.
/// </summary>
public record ResolvedOutputTarget(ScreenBounds Bounds, string? HostDeviceName, bool IsTransparentOverlay);

/// <summary>
/// Enumerates physical displays and changes their resolution via the Win32 display API.
/// Raw P/Invoke is used instead of System.Windows.Forms.Screen so this stays usable
/// from a UI-agnostic core library.
/// </summary>
public static class DisplayManager
{
    public static List<DisplayInfo> GetDisplays()
    {
        var result = new List<DisplayInfo>();
        var device = new DISPLAY_DEVICE();
        device.cb = Marshal.SizeOf(device);

        for (uint i = 0; EnumDisplayDevices(null, i, ref device, 0); i++, device.cb = Marshal.SizeOf(device))
        {
            bool attached = (device.StateFlags & DISPLAY_DEVICE_ATTACHED_TO_DESKTOP) != 0;
            if (!attached) continue;

            var mode = new DEVMODE();
            mode.dmSize = (short)Marshal.SizeOf(mode);
            if (!EnumDisplaySettings(device.DeviceName, ENUM_CURRENT_SETTINGS, ref mode)) continue;

            bool isPrimary = (device.StateFlags & DISPLAY_DEVICE_PRIMARY_DEVICE) != 0;
            result.Add(new DisplayInfo(
                device.DeviceName,
                GetMonitorFriendlyName(device.DeviceName),
                ParseDisplayNumber(device.DeviceName) ?? result.Count + 1,
                mode.dmPositionX,
                mode.dmPositionY,
                mode.dmPelsWidth,
                mode.dmPelsHeight,
                isPrimary));
        }

        return result;
    }

    /// <summary>
    /// Extracts the number Windows itself assigns to this display from its device name
    /// (e.g. "\\.\DISPLAY3" -> 3). EnumDisplayDevices does NOT necessarily enumerate devices
    /// in that same order, so numbering displays by enumeration order ("1st found", "2nd
    /// found", ...) can label a monitor differently from what the user sees in Windows'
    /// own Display Settings - this keeps our "Monitor N" label matching that number instead.
    /// </summary>
    private static int? ParseDisplayNumber(string deviceName)
    {
        var match = Regex.Match(deviceName, @"DISPLAY(\d+)", RegexOptions.IgnoreCase);
        return match.Success && int.TryParse(match.Groups[1].Value, out var number) ? number : null;
    }

    /// <summary>
    /// Returns the monitor's own friendly name (e.g. "Dell S2721DGF" or "Generic PnP Monitor"),
    /// as opposed to the graphics adapter name EnumDisplayDevices(null, ...) would give.
    /// </summary>
    private static string GetMonitorFriendlyName(string adapterDeviceName)
    {
        var monitor = new DISPLAY_DEVICE();
        monitor.cb = Marshal.SizeOf(monitor);
        if (EnumDisplayDevices(adapterDeviceName, 0, ref monitor, 0) && !string.IsNullOrWhiteSpace(monitor.DeviceString))
            return monitor.DeviceString;

        return "Schermo";
    }

    /// <summary>
    /// Resolves a cue's target id (a physical DisplayInfo.Id, or a VirtualDisplay.Id) into the
    /// absolute on-screen rectangle it should render into. Returns null if the id isn't found
    /// (e.g. the monitor got unplugged, or the virtual display's host monitor is gone).
    /// </summary>
    public static ScreenBounds? ResolveBounds(string targetId, IEnumerable<DisplayInfo> physicalDisplays, IEnumerable<VirtualDisplay> virtualDisplays)
    {
        var physicalList = physicalDisplays as IReadOnlyList<DisplayInfo> ?? physicalDisplays.ToList();

        var physical = physicalList.FirstOrDefault(d => d.Id == targetId);
        if (physical is not null)
            return new ScreenBounds(physical.X, physical.Y, physical.Width, physical.Height);

        var virtualDisplay = virtualDisplays.FirstOrDefault(v => v.Id == targetId);
        if (virtualDisplay is not null)
        {
            // No silent fallback to "any" monitor here: that would usually be the primary one.
            var host = physicalList.FirstOrDefault(d => d.DeviceName == virtualDisplay.HostDeviceName);
            if (host is null) return null;
            return new ScreenBounds(host.X + virtualDisplay.OffsetX, host.Y + virtualDisplay.OffsetY, virtualDisplay.Width, virtualDisplay.Height);
        }

        return null;
    }

    /// <summary>
    /// Where a video cue should actually play. If its target is gone (projector unplugged, or
    /// renumbered by Windows after re-plugging), falls back to the first secondary monitor —
    /// but NEVER to the primary one: that's the operator's screen, and a topmost fullscreen
    /// video there would hide the cue list mid-show. Returns null (cue not played) instead.
    /// A cue with no target at all plays on the primary monitor (single-screen rehearsal).
    /// </summary>
    /// <param name="warning">Set when the cue's own target couldn't be used; null otherwise.</param>
    public static ScreenBounds? ResolveOutputBounds(string? targetId, IEnumerable<DisplayInfo> physicalDisplays,
        IEnumerable<VirtualDisplay> virtualDisplays, out string? warning)
    {
        warning = null;
        var physicalList = physicalDisplays as IReadOnlyList<DisplayInfo> ?? physicalDisplays.ToList();

        if (string.IsNullOrEmpty(targetId))
        {
            var primary = physicalList.FirstOrDefault(d => d.IsPrimary) ?? physicalList.FirstOrDefault();
            return primary is null ? null : new ScreenBounds(primary.X, primary.Y, primary.Width, primary.Height);
        }

        var resolved = ResolveBounds(targetId, physicalList, virtualDisplays);
        if (resolved is not null) return resolved;

        var secondary = physicalList.FirstOrDefault(d => !d.IsPrimary);
        if (secondary is not null)
        {
            warning = $"schermo della cue non trovato, uso {secondary.Label}";
            return new ScreenBounds(secondary.X, secondary.Y, secondary.Width, secondary.Height);
        }

        warning = "schermo della cue non trovato e nessun monitor secondario collegato";
        return null;
    }

    /// <summary>
    /// Which physical monitor a resolved output rectangle actually lands on, found by its
    /// top-left point. Works for every case ResolveOutputBounds can return - a physical target,
    /// a virtual display (its host), or the "no secondary monitor" fallback - without having to
    /// duplicate that method's own fallback logic here.
    /// </summary>
    public static DisplayInfo? FindHostDisplay(ScreenBounds bounds, IEnumerable<DisplayInfo> physicalDisplays)
        => physicalDisplays.FirstOrDefault(d =>
            bounds.X >= d.X && bounds.X < d.X + d.Width &&
            bounds.Y >= d.Y && bounds.Y < d.Y + d.Height);

    /// <summary>
    /// Everything PlayAsync needs to open an output window: the rectangle (via
    /// ResolveOutputBounds, unchanged), which physical monitor it lands on, and whether the
    /// target itself is a transparent-overlay VirtualDisplay (false for anything else, including
    /// when the fallback silently redirects to a secondary monitor - at that point it's not the
    /// original target anymore, so any transparency it asked for no longer applies).
    /// </summary>
    public static ResolvedOutputTarget? ResolveOutputTarget(string? targetId, IEnumerable<DisplayInfo> physicalDisplays,
        IEnumerable<VirtualDisplay> virtualDisplays, out string? warning)
    {
        var physicalList = physicalDisplays as IReadOnlyList<DisplayInfo> ?? physicalDisplays.ToList();
        var virtualList = virtualDisplays as IReadOnlyList<VirtualDisplay> ?? virtualDisplays.ToList();

        var bounds = ResolveOutputBounds(targetId, physicalList, virtualList, out warning);
        if (bounds is null) return null;

        // Only true when targetId's own direct (non-fallback) lookup actually succeeded: once
        // ResolveOutputBounds has fallen back to a secondary monitor, the original target (and
        // whatever transparency it asked for) is no longer what's being shown.
        var isTransparentOverlay = warning is null
            && (virtualList.FirstOrDefault(v => v.Id == targetId)?.IsTransparentOverlay ?? false);
        var hostDeviceName = FindHostDisplay(bounds, physicalList)?.DeviceName;
        return new ResolvedOutputTarget(bounds, hostDeviceName, isTransparentOverlay);
    }

    public static List<DisplayMode> GetAvailableModes(string deviceName)
    {
        var modes = new List<DisplayMode>();
        var mode = new DEVMODE();
        mode.dmSize = (short)Marshal.SizeOf(mode);

        for (int i = 0; EnumDisplaySettings(deviceName, i, ref mode); i++)
        {
            modes.Add(new DisplayMode(mode.dmPelsWidth, mode.dmPelsHeight, mode.dmDisplayFrequency, mode.dmBitsPerPel));
        }

        return modes
            .GroupBy(m => (m.Width, m.Height, m.RefreshRate, m.BitsPerPixel))
            .Select(g => g.First())
            .OrderByDescending(m => m.Width * m.Height)
            .ThenByDescending(m => m.RefreshRate)
            .ToList();
    }

    /// <returns>true if the resolution change succeeded.</returns>
    public static bool ChangeResolution(string deviceName, int width, int height, int refreshRate = 0, int bitsPerPixel = 32)
    {
        var mode = new DEVMODE();
        mode.dmSize = (short)Marshal.SizeOf(mode);
        if (!EnumDisplaySettings(deviceName, ENUM_CURRENT_SETTINGS, ref mode)) return false;

        mode.dmPelsWidth = width;
        mode.dmPelsHeight = height;
        mode.dmBitsPerPel = bitsPerPixel;
        if (refreshRate > 0) mode.dmDisplayFrequency = refreshRate;
        mode.dmFields = DM_PELSWIDTH | DM_PELSHEIGHT | DM_BITSPERPEL | (refreshRate > 0 ? DM_DISPLAYFREQUENCY : 0);

        int result = ChangeDisplaySettingsEx(deviceName, ref mode, IntPtr.Zero, CDS_UPDATEREGISTRY, IntPtr.Zero);
        return result == DISP_CHANGE_SUCCESSFUL;
    }

    public static bool TestResolution(string deviceName, int width, int height, int refreshRate = 0, int bitsPerPixel = 32)
    {
        var mode = new DEVMODE();
        mode.dmSize = (short)Marshal.SizeOf(mode);
        if (!EnumDisplaySettings(deviceName, ENUM_CURRENT_SETTINGS, ref mode)) return false;

        mode.dmPelsWidth = width;
        mode.dmPelsHeight = height;
        mode.dmBitsPerPel = bitsPerPixel;
        if (refreshRate > 0) mode.dmDisplayFrequency = refreshRate;
        mode.dmFields = DM_PELSWIDTH | DM_PELSHEIGHT | DM_BITSPERPEL | (refreshRate > 0 ? DM_DISPLAYFREQUENCY : 0);

        int result = ChangeDisplaySettingsEx(deviceName, ref mode, IntPtr.Zero, CDS_TEST, IntPtr.Zero);
        return result == DISP_CHANGE_SUCCESSFUL;
    }

    // ---- P/Invoke ----

    private const int ENUM_CURRENT_SETTINGS = -1;
    private const uint DISPLAY_DEVICE_ATTACHED_TO_DESKTOP = 0x1;
    private const uint DISPLAY_DEVICE_PRIMARY_DEVICE = 0x4;
    private const uint CDS_UPDATEREGISTRY = 0x01;
    private const uint CDS_TEST = 0x02;
    private const int DISP_CHANGE_SUCCESSFUL = 0;
    private const uint DM_PELSWIDTH = 0x80000;
    private const uint DM_PELSHEIGHT = 0x100000;
    private const uint DM_BITSPERPEL = 0x40000;
    private const uint DM_DISPLAYFREQUENCY = 0x400000;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private struct DISPLAY_DEVICE
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public uint StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public short dmSpecVersion;
        public short dmDriverVersion;
        public short dmSize;
        public short dmDriverExtra;
        public uint dmFields;
        public int dmPositionX;
        public int dmPositionY;
        public uint dmDisplayOrientation;
        public uint dmDisplayFixedOutput;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel;
        public int dmPelsWidth;
        public int dmPelsHeight;
        public int dmDisplayFlags;
        public int dmDisplayFrequency;
        public int dmICMMethod;
        public int dmICMIntent;
        public int dmMediaType;
        public int dmDitherType;
        public int dmReserved1;
        public int dmReserved2;
        public int dmPanningWidth;
        public int dmPanningHeight;
    }

    [DllImport("user32.dll", CharSet = CharSet.Ansi)]
    private static extern bool EnumDisplayDevices(string? lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Ansi)]
    private static extern bool EnumDisplaySettings(string deviceName, int modeNum, ref DEVMODE devMode);

    [DllImport("user32.dll", CharSet = CharSet.Ansi)]
    private static extern int ChangeDisplaySettingsEx(string deviceName, ref DEVMODE devMode, IntPtr hwnd, uint flags, IntPtr param);
}
