using System.Globalization;
using System.Windows;
using System.Windows.Data;
using LiveCue.Core.Models;

namespace LiveCue.App;

/// <summary>
/// Shows/hides an inspector tab (or a section within one) based on what the selected cue can
/// actually do - e.g. a Fade/Stop cue has no file, so it gets no "Video"/"Audio" tab at all.
/// ConverterParameter picks which check: "Video" (has a target screen: Video or Image cue),
/// "Filters" (has LibVLC color correction: Video cue only), "Audio" (plays through LibVLC:
/// Audio or Video cue), "InOut" (same as Audio - trimmable).
/// </summary>
public class CueKindVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var visible = (value, parameter as string) switch
        {
            (VideoCue, "Video" or "Filters" or "Audio" or "InOut") => true,
            (ImageCue, "Video") => true,
            (AudioCue, "Audio" or "InOut") => true,
            _ => false,
        };
        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// The Rotazione slider has no visible effect on a VideoCue: LibVLC's video surface is a native
/// Win32 child window (HwndHost), and WPF can resize/reposition that window (so Zoom/Posizione
/// work) but cannot rotate it - that needs actual pixel compositing, which a plain child hwnd
/// doesn't support (the WPF "airspace" limitation). An ImageCue has no such window (just a WPF
/// Image control), so rotation works there. Disable the control rather than leave it silently
/// doing nothing.
/// </summary>
public class RotationSupportedConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is not VideoCue;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Prefixes a cue type's name with the same glyph already used on its "Add ... Cue" toolbar
/// button, so the "Tipo" column reads at a glance in a long list instead of needing the text.
/// </summary>
public class CueTypeIconConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var icon = (value as CueType?) switch
        {
            CueType.Video => "▶",  // ▶, matches "Video Cue"
            CueType.Audio => "♪",  // ♪, matches "Audio Cue"
            CueType.Image => "▭",  // ▭, matches "Image Cue"
            CueType.Fade => "◐",   // ◐, matches "Fade Cue"
            CueType.Stop => "■",   // ■, matches "Stop Cue"
            _ => null,
        };
        return icon is null ? $"{value}" : $"{icon} {value}";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
