using System.Globalization;

namespace LiveCue.Core.Services;

/// <summary>
/// Parses/formats the times typed in the cue list (pre-wait, post-wait). A bare number means
/// SECONDS ("5", "2.5", "2,5"); "m:ss" and "h:mm:ss" are accepted too. The default TimeSpan
/// parsing would read "5" as five DAYS, which silently blocks the cue.
/// </summary>
public static class TimeInput
{
    public static string Format(TimeSpan value)
    {
        var seconds = Math.Round(value.TotalSeconds, 2);
        return seconds.ToString("0.##", CultureInfo.CurrentCulture);
    }

    public static bool TryParse(string? text, out TimeSpan value)
    {
        value = TimeSpan.Zero;
        var input = (text ?? "").Trim().Replace(',', '.');
        if (input.Length == 0) return true;

        double totalSeconds;
        if (input.Contains(':'))
        {
            // "m:ss(.f)" or "h:mm:ss(.f)"
            var parts = input.Split(':');
            if (parts.Length > 3) return false;
            totalSeconds = 0;
            foreach (var part in parts)
            {
                if (!double.TryParse(part, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number))
                    return false;
                totalSeconds = totalSeconds * 60 + number;
            }
        }
        else if (!double.TryParse(input, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out totalSeconds))
        {
            return false;
        }

        if (totalSeconds > TimeSpan.FromDays(1).TotalSeconds) return false;
        value = TimeSpan.FromSeconds(totalSeconds);
        return true;
    }
}
