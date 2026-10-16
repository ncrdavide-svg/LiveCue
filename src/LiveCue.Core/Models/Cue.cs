using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace LiveCue.Core.Models;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$cueType")]
[JsonDerivedType(typeof(VideoCue), "video")]
[JsonDerivedType(typeof(AudioCue), "audio")]
[JsonDerivedType(typeof(ImageCue), "image")]
[JsonDerivedType(typeof(FadeCue), "fade")]
[JsonDerivedType(typeof(StopCue), "stop")]
public abstract class Cue : INotifyPropertyChanged
{
    [JsonInclude]
    public Guid Id { get; private set; } = Guid.NewGuid();
    public abstract CueType Type { get; }

    private string _number = string.Empty;
    public string Number { get => _number; set => SetField(ref _number, value); }

    private string _name = string.Empty;
    public string Name { get => _name; set => SetField(ref _name, value); }

    private string _notes = string.Empty;
    public string Notes { get => _notes; set => SetField(ref _notes, value); }

    private TimeSpan _preWait = TimeSpan.Zero;
    public TimeSpan PreWait { get => _preWait; set => SetField(ref _preWait, value); }

    private TimeSpan _postWait = TimeSpan.Zero;
    public TimeSpan PostWait { get => _postWait; set => SetField(ref _postWait, value); }

    private ContinueMode _continueMode = ContinueMode.DoNotContinue;
    public ContinueMode ContinueMode { get => _continueMode; set => SetField(ref _continueMode, value); }

    private bool _isArmed = true;
    public bool IsArmed { get => _isArmed; set => SetField(ref _isArmed, value); }

    /// <summary>Total length of the media file, once known (populated asynchronously after the cue is added).</summary>
    private TimeSpan? _duration;
    public TimeSpan? Duration { get => _duration; set => SetField(ref _duration, value); }

    /// <summary>Time left to play, updated live while the cue is playing; null when not playing. Not persisted.</summary>
    private TimeSpan? _remainingTime;
    [JsonIgnore]
    public TimeSpan? RemainingTime { get => _remainingTime; set => SetField(ref _remainingTime, value); }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
