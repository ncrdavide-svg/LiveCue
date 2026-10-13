using System.Runtime.InteropServices;
using System.Windows;

namespace LiveCue.App;

/// <summary>
/// Thin wrapper over the DWM thumbnail API (the same mechanism behind Alt+Tab and Task View):
/// lets one window show a live, GPU-composited mirror of another window's content, without any
/// manual screen-capture polling.
/// </summary>
internal static class DwmThumbnail
{
    public static IntPtr Register(IntPtr destHwnd, IntPtr srcHwnd)
    {
        _ = DwmRegisterThumbnail(destHwnd, srcHwnd, out var thumbnailId);
        return thumbnailId;
    }

    public static void Unregister(IntPtr thumbnailId)
    {
        if (thumbnailId != IntPtr.Zero)
            DwmUnregisterThumbnail(thumbnailId);
    }

    /// <summary>Positions/shows the thumbnail. <paramref name="destinationPx"/> is in the destination window's client-area device pixels.</summary>
    public static void SetRect(IntPtr thumbnailId, Int32Rect destinationPx)
    {
        if (thumbnailId == IntPtr.Zero) return;

        var props = new DWM_THUMBNAIL_PROPERTIES
        {
            dwFlags = DWM_TNP_RECTDESTINATION | DWM_TNP_VISIBLE | DWM_TNP_OPACITY,
            rcDestination = new RECT
            {
                Left = destinationPx.X,
                Top = destinationPx.Y,
                Right = destinationPx.X + destinationPx.Width,
                Bottom = destinationPx.Y + destinationPx.Height,
            },
            fVisible = true,
            opacity = 255,
        };
        DwmUpdateThumbnailProperties(thumbnailId, ref props);
    }

    // ---- P/Invoke ----

    private const int DWM_TNP_RECTDESTINATION = 0x1;
    private const int DWM_TNP_OPACITY = 0x4;
    private const int DWM_TNP_VISIBLE = 0x8;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DWM_THUMBNAIL_PROPERTIES
    {
        public int dwFlags;
        public RECT rcDestination;
        public RECT rcSource;
        public byte opacity;
        [MarshalAs(UnmanagedType.Bool)] public bool fVisible;
        [MarshalAs(UnmanagedType.Bool)] public bool fSourceClientAreaOnly;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmRegisterThumbnail(IntPtr dest, IntPtr src, out IntPtr thumbnailId);

    [DllImport("dwmapi.dll")]
    private static extern int DwmUnregisterThumbnail(IntPtr thumbnailId);

    [DllImport("dwmapi.dll")]
    private static extern int DwmUpdateThumbnailProperties(IntPtr thumbnailId, ref DWM_THUMBNAIL_PROPERTIES props);
}
