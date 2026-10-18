using System.Globalization;
using System.IO;
using System.Windows.Data;

namespace LiveCue.App;

/// <summary>File path -&gt; human-readable size ("4,2 MB"), for the inspector's "Informazioni" tab.</summary>
public class FileSizeConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || string.IsNullOrEmpty(path) || !File.Exists(path)) return "";

        double bytes = new FileInfo(path).Length;
        string[] units = { "B", "KB", "MB", "GB" };
        var unit = 0;
        while (bytes >= 1024 && unit < units.Length - 1)
        {
            bytes /= 1024;
            unit++;
        }
        return $"{bytes.ToString(unit == 0 ? "F0" : "F1", culture)} {units[unit]}";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
