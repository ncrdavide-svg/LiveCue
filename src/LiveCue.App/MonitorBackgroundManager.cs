using System.Linq;
using LiveCue.Core.Services;

namespace LiveCue.App;

/// <summary>
/// Keeps one borderless black <see cref="MonitorBackgroundWindow"/> glued to each currently
/// "Attivo" physical monitor, so an output screen reads as ready-for-the-show instead of showing
/// the Windows desktop, and goes back to black on its own once a video/image cue covering it ends
/// (its own window closes, revealing this one underneath).
/// NEVER applies to the primary monitor, enabled or not - that's the operator's own screen, and a
/// Topmost black window there would lock them out of LiveCue itself (the exact lockout the
/// ESC/PANIC hotkey fix was built to prevent for video cues, see MainWindow.xaml.cs).
/// </summary>
public class MonitorBackgroundManager
{
    private readonly Dictionary<string, MonitorBackgroundWindow> _windows = new();

    /// <summary>Call after every display refresh: creates/repositions/closes background windows to match.</summary>
    public void Sync(IEnumerable<DisplayInfo> enabledDisplays)
    {
        var current = enabledDisplays.Where(d => !d.IsPrimary).ToDictionary(d => d.DeviceName);

        foreach (var deviceName in _windows.Keys.Except(current.Keys).ToList())
        {
            _windows[deviceName].Close();
            _windows.Remove(deviceName);
        }

        foreach (var (deviceName, display) in current)
        {
            var bounds = new ScreenBounds(display.X, display.Y, display.Width, display.Height);
            if (_windows.TryGetValue(deviceName, out var window))
                window.MoveTo(bounds);
            else
            {
                window = new MonitorBackgroundWindow(bounds);
                window.Show();
                _windows[deviceName] = window;
            }
        }
    }
}
