using System.Globalization;
using System.Windows;
using System.Windows.Data;
using LiveCue.Core.Services;

namespace LiveCue.App;

/// <summary>Shows/edits a TimeSpan as seconds in the cue list (see <see cref="TimeInput"/>). Invalid text is rejected.</summary>
public class SecondsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is TimeSpan time ? TimeInput.Format(time) : "";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        TimeInput.TryParse(value as string, out var time) ? time : DependencyProperty.UnsetValue;
}
