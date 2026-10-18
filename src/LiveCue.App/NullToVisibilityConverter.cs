using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace LiveCue.App;

/// <summary>Shows the cue inspector only while a cue is selected (CueList.SelectedCue is non-null).</summary>
public class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is null ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
