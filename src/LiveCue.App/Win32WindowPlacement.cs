using System.Runtime.InteropServices;
using LiveCue.Core.Services;

namespace LiveCue.App;

/// <summary>
/// Shared Win32 helpers for the borderless overlay windows (video output, monitor identify):
/// exact pixel positioning regardless of per-monitor DPI scaling, and a guarantee that the
/// window can never steal keyboard focus from the main window. See VideoOutputWindow for why
/// both of these matter.
/// </summary>
internal static class Win32WindowPlacement
{
    public static void ApplyPixelBounds(IntPtr hwnd, ScreenBounds bounds)
    {
        if (hwnd == IntPtr.Zero) return;
        SetWindowPos(hwnd, IntPtr.Zero, bounds.X, bounds.Y, bounds.Width, bounds.Height, SwpNoZOrder | SwpNoActivate);
    }

    public static void SetNoActivate(IntPtr hwnd)
    {
        var exStyle = GetWindowLongPtr(hwnd, GwlExStyle);
        SetWindowLongPtr(hwnd, GwlExStyle, new IntPtr(exStyle.ToInt64() | WsExNoActivate));
    }

    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const int GwlExStyle = -20;
    private const long WsExNoActivate = 0x08000000;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
}
