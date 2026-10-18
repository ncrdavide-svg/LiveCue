using System.Globalization;
using System.Windows;
using System.Windows.Data;
using LiveCue.Core.Services;

namespace LiveCue.App;

/// <summary>
/// Shows/edits a nullable TimeSpan (TrimStart/TrimEnd) as seconds - same text format as
/// <see cref="SecondsConverter"/>, but an empty field means "clear it" (null = no mark
/// in/out), not zero. SecondsConverter can't be reused as-is: TimeInput.TryParse("") already
/// succeeds with TimeSpan.Zero (by design, for PreWait/PostWait, which have no "unset" state),
/// so reusing it here would turn an emptied "Fine" field into "stop at 0s" instead of "no mark
/// out" - silently killing playback instead of clearing the trim.
/// </summary>
public class NullableSecondsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is TimeSpan time ? TimeInput.Format(time) : "";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var text = (value as string ?? "").Trim();
        if (text.Length == 0) return null!;
        return TimeInput.TryParse(text, out var time) ? time : DependencyProperty.UnsetValue;
    }
}
