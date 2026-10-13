using System.Globalization;
using System.Windows.Data;
using LiveCue.Core.Models;

namespace LiveCue.App;

/// <summary>
/// Disables the "Continue" combo box for the last cue in the list: there's no next cue for
/// it to chain into, so "Continua"/"Parti insieme" would be meaningless there.
/// Bindings: [0] the row's Cue, [1] the CueList.Cues collection, [2] its Count (just to force
/// re-evaluation when cues are added/removed, since the collection reference itself doesn't change).
/// </summary>
public class IsNotLastCueConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Length < 2 || values[0] is not Cue cue || values[1] is not IList<Cue> cues)
            return true;

        var index = cues.IndexOf(cue);
        return index < 0 || index != cues.Count - 1;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
