using System.Globalization;
using System.Windows.Data;

namespace LiveCue.App;

/// <summary>MediaTrack.Bitrate (bits/second, LibVLC's own unit) -&gt; "320 kbit/s", for the inspector.</summary>
public class BitrateConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is uint bps and > 0 ? $"{bps / 1000} kbit/s" : "";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
