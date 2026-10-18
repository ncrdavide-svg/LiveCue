using System.IO;
using System.Text.Json.Serialization;

namespace LiveCue.Core.Models;

/// <summary>Base for cues that play a media file (audio or video).</summary>
public abstract class MediaCue : Cue, IFileCue
{
    private string _filePath = string.Empty;
    public string FilePath
    {
        get => _filePath;
        set
        {
            SetField(ref _filePath, value);
            FileMissing = _filePath.Length > 0 && !File.Exists(_filePath);
        }
    }

    private double _volume = 1.0;
    public double Volume { get => _volume; set => SetField(ref _volume, value); }

    private bool _loop;
    public bool Loop { get => _loop; set => SetField(ref _loop, value); }

    /// <summary>LibVLC audio output device id (see MediaPlayerCuePlayer.EnumerateAudioOutputDevices). Null = system default.</summary>
    private string? _audioOutputDeviceId;
    public string? AudioOutputDeviceId { get => _audioOutputDeviceId; set => SetField(ref _audioOutputDeviceId, value); }

    /// <summary>Mark in: skips this much from the start of the file. Null = play from the beginning.</summary>
    private TimeSpan? _trimStart;
    public TimeSpan? TrimStart { get => _trimStart; set => SetField(ref _trimStart, value); }

    /// <summary>Mark out: stops this far into the file. Null = play to the end.</summary>
    private TimeSpan? _trimEnd;
    public TimeSpan? TrimEnd { get => _trimEnd; set => SetField(ref _trimEnd, value); }

    // --- Technical info for the inspector's "Informazioni" tab, read from the file by
    // MediaPlayerCuePlayer.LoadMediaInfoAsync - not part of the show, so not persisted. ---

    private uint? _bitrate;
    [JsonIgnore]
    public uint? Bitrate { get => _bitrate; set => SetField(ref _bitrate, value); }

    private string? _codec;
    [JsonIgnore]
    public string? Codec { get => _codec; set => SetField(ref _codec, value); }

    private uint? _audioChannels;
    [JsonIgnore]
    public uint? AudioChannels { get => _audioChannels; set => SetField(ref _audioChannels, value); }

    private uint? _audioSampleRate;
    [JsonIgnore]
    public uint? AudioSampleRate { get => _audioSampleRate; set => SetField(ref _audioSampleRate, value); }
}
