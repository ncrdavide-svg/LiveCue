using System.Windows;
using LibVLCSharp.Shared;
using LiveCue.Core.Models;
using LiveCue.Core.Services;

namespace LiveCue.App;

/// <summary>
/// ICuePlayer implementation backed by LibVLC. Each video cue gets its own borderless
/// <see cref="VideoOutputWindow"/> sized to its target (a physical monitor, fullscreen, or a
/// virtual display region). Windows are never shared between cues: when two video cues target
/// the same screen, the newer one's window simply sits on top as its own layer — the earlier
/// cue keeps playing underneath, unaffected, and becomes visible again once the newer one ends.
/// Audio cues just play through a plain MediaPlayer (no window).
/// NOTE: assumes PlayAsync is always invoked from the WPF UI thread (true as long as the
/// CueEngine is driven from a button-click handler), since VideoOutputWindow is a Window.
/// </summary>
public class MediaPlayerCuePlayer : ICuePlayer
{
    private readonly LibVLC _libVlc;
    private readonly Func<string?, ScreenBounds?> _resolveBounds;
    private readonly Dictionary<Guid, MediaPlayer> _activePlayers = new();
    private readonly Dictionary<Guid, VideoOutputWindow> _activeWindows = new();

    /// <summary>Fired right after a cue's video output window is shown — lets a monitor-preview window mirror it live.</summary>
    public event Action<Guid, VideoOutputWindow>? VideoWindowOpened;

    /// <summary>Fired right before a cue's video output window is closed.</summary>
    public event Action<Guid>? VideoWindowClosed;

    public MediaPlayerCuePlayer(LibVLC libVlc, Func<string?, ScreenBounds?> resolveBounds)
    {
        _libVlc = libVlc;
        _resolveBounds = resolveBounds;
    }

    public Task PlayAsync(Cue cue, CancellationToken ct) => cue switch
    {
        VideoCue v => PlayVideoAsync(v, ct),
        AudioCue a => PlayAudioAsync(a, ct),
        _ => Task.CompletedTask,
    };

    public void Stop(Cue cue)
    {
        if (_activePlayers.TryGetValue(cue.Id, out var player))
            player.Stop();
    }

    public void StopAll()
    {
        foreach (var player in _activePlayers.Values.ToList())
            player.Stop();

        // Hide every video output window immediately, rather than waiting for each cue's own
        // async cleanup to run — the panic button should clear the screen synchronously.
        foreach (var window in _activeWindows.Values.ToList())
            window.Hide();
    }

    /// <summary>
    /// Parses just enough of the file to learn its duration and stores it on the cue, for the
    /// cue list's "Durata" column. Fire-and-forget from the caller; failures just leave it null.
    /// </summary>
    public async Task LoadDurationAsync(Cue cue, string filePath)
    {
        using var media = new Media(_libVlc, new Uri(filePath));
        var status = await media.Parse(MediaParseOptions.ParseLocal);
        if (status == MediaParsedStatus.Done && media.Duration > 0)
            cue.Duration = TimeSpan.FromMilliseconds(media.Duration);
    }

    private async Task PlayVideoAsync(VideoCue cue, CancellationToken ct)
    {
        var bounds = _resolveBounds(cue.TargetDisplayId);
        if (bounds is null) return;

        // Always a fresh window, even if another cue is already showing on the same screen:
        // it becomes a new layer on top, the earlier cue keeps playing untouched underneath.
        var window = new VideoOutputWindow(bounds);
        var media = new Media(_libVlc, new Uri(cue.FilePath));
        if (cue.Loop) media.AddOption(":input-repeat=65535");

        var player = new MediaPlayer(media) { Volume = ToVlcVolume(cue.Volume) };
        window.Attach(player);
        window.Show();
        window.Activate(); // among same-Topmost windows, the most recently activated one is frontmost
        _activePlayers[cue.Id] = player;
        _activeWindows[cue.Id] = window;
        TrackRemainingTime(player, cue);
        VideoWindowOpened?.Invoke(cue.Id, window);

        var tcs = new TaskCompletionSource();
        player.EndReached += (_, _) => tcs.TrySetResult();
        using var registration = ct.Register(() =>
        {
            player.Stop();
            tcs.TrySetResult();
        });

        try
        {
            player.Play();
            await tcs.Task;
        }
        finally
        {
            _activePlayers.Remove(cue.Id);
            _activeWindows.Remove(cue.Id);
            cue.RemainingTime = null;
            VideoWindowClosed?.Invoke(cue.Id);
            window.Detach();
            window.Close();
            player.Dispose();
            media.Dispose();
        }
    }

    private async Task PlayAudioAsync(AudioCue cue, CancellationToken ct)
    {
        var media = new Media(_libVlc, new Uri(cue.FilePath));
        if (cue.Loop) media.AddOption(":input-repeat=65535");

        var player = new MediaPlayer(media) { Volume = ToVlcVolume(cue.Volume) };
        _activePlayers[cue.Id] = player;
        TrackRemainingTime(player, cue);

        var tcs = new TaskCompletionSource();
        player.EndReached += (_, _) => tcs.TrySetResult();
        using var registration = ct.Register(() =>
        {
            player.Stop();
            tcs.TrySetResult();
        });

        try
        {
            player.Play();
            await tcs.Task;
        }
        finally
        {
            _activePlayers.Remove(cue.Id);
            cue.RemainingTime = null;
            player.Dispose();
            media.Dispose();
        }
    }

    /// <summary>Keeps Cue.RemainingTime updated live while this player is playing (marshalled to the UI thread).</summary>
    private static void TrackRemainingTime(MediaPlayer player, Cue cue)
    {
        player.TimeChanged += (_, args) =>
        {
            var length = player.Length;
            if (length <= 0) return;

            var remaining = TimeSpan.FromMilliseconds(Math.Max(0, length - args.Time));
            Application.Current?.Dispatcher.BeginInvoke(() => cue.RemainingTime = remaining);
        };
    }

    private static int ToVlcVolume(double volume01) => (int)Math.Clamp(volume01 * 100, 0, 100);
}
