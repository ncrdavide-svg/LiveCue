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
