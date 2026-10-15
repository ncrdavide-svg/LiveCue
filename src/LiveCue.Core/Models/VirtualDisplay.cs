using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace LiveCue.Core.Models;

/// <summary>
/// A user-defined rectangular output region placed on top of a real monitor — e.g. a
/// 1280x720 "stage" window living in the corner of a 4K screen instead of taking it over
/// fullscreen, or any arbitrary custom resolution unrelated to what the monitor itself supports.
/// </summary>
public class VirtualDisplay : INotifyPropertyChanged
{
    // Persisted (and restored on load) so cues saved with this target stay linked to it.
    [JsonInclude]
    public string Id { get; private set; } = "virtual:" + Guid.NewGuid();

    private string _name = "Virtuale 1";
    public string Name { get => _name; set => SetField(ref _name, value); }

    /// <summary>DisplayInfo.DeviceName of the physical monitor this virtual display lives on.</summary>
    private string _hostDeviceName = "";
    public string HostDeviceName { get => _hostDeviceName; set => SetField(ref _hostDeviceName, value); }

    private int _offsetX;
    public int OffsetX { get => _offsetX; set => SetField(ref _offsetX, value); }

    private int _offsetY;
    public int OffsetY { get => _offsetY; set => SetField(ref _offsetY, value); }

    private int _width = 1920;
    public int Width { get => _width; set => SetField(ref _width, value); }

    private int _height = 1080;
    public int Height { get => _height; set => SetField(ref _height, value); }

    [JsonIgnore]
    public string Label => $"{Name} ({Width}x{Height} virtuale)";

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        if (propertyName != nameof(Label))
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Label)));
    }
}
