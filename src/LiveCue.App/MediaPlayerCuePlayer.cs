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
    /// <summary>A jump back in playback time larger than this means a looping cue just wrapped around.</summary>
    private const long LoopWrapThresholdMs = 1000;

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

    public Task PlayAsync(Cue cue, CancellationToken ct) => cue is MediaCue mediaCue
        ? PlayMediaAsync(mediaCue, ct)
        : Task.CompletedTask;

    public void Stop(Cue cue)
    {
        if (_activePlayers.TryGetValue(cue.Id, out var player))
            player.Stop();
    }

    public void StopAll()
    {
        // Each player's Stopped event then runs that cue's own cleanup (closing its window,
        // disposing the player) — even for cues whose GO chain has already finished.
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

    /// <returns>A task that completes at the end of the first pass (see <see cref="ICuePlayer.PlayAsync"/>).</returns>
    private Task PlayMediaAsync(MediaCue cue, CancellationToken ct)
    {
        VideoOutputWindow? window = null;
        if (cue is VideoCue videoCue)
        {
            var bounds = _resolveBounds(videoCue.TargetDisplayId);
            if (bounds is null) return Task.CompletedTask;
            // Always a fresh window, even if another cue is already showing on the same screen:
            // it becomes a new layer on top, the earlier cue keeps playing untouched underneath.
            window = new VideoOutputWindow(bounds);
        }

        var media = new Media(_libVlc, new Uri(cue.FilePath));
        if (cue.Loop) media.AddOption(":input-repeat=65535");
        var player = new MediaPlayer(media) { Volume = ToVlcVolume(cue.Volume) };

        // Continuations must not run inline on LibVLC's event thread (calling back into the
        // player from there can deadlock), hence RunContinuationsAsynchronously.
        var firstPassDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        void Complete()
        {
            firstPassDone.TrySetResult();
            finished.TrySetResult();
        }

        player.EndReached += (_, _) => Complete();
        // Stop()/StopAll()/PANIC: VLC raises Stopped, not EndReached.
        player.Stopped += (_, _) => Complete();

        long lastTimeMs = 0;
        player.TimeChanged += (_, args) =>
        {
            if (cue.Loop && args.Time + LoopWrapThresholdMs < lastTimeMs)
                firstPassDone.TrySetResult();
            lastTimeMs = args.Time;

            var length = player.Length;
            if (length <= 0) return;
            var remaining = TimeSpan.FromMilliseconds(Math.Max(0, length - args.Time));
            Application.Current?.Dispatcher.BeginInvoke(() => cue.RemainingTime = remaining);
        };

        _activePlayers[cue.Id] = player;
        if (window is not null)
        {
            window.Attach(player);
            window.Show();
            window.Activate(); // among same-Topmost windows, the most recently activated one is frontmost
            _activeWindows[cue.Id] = window;
            VideoWindowOpened?.Invoke(cue.Id, window);
        }

        _ = RunUntilFinishedAsync(cue, player, media, window, finished.Task, Complete, ct);
        player.Play();
        return firstPassDone.Task;
    }

    /// <summary>Owns the player for its whole life (which, for a loop, outlasts the first pass) and cleans up at the end.</summary>
    private async Task RunUntilFinishedAsync(MediaCue cue, MediaPlayer player, Media media, VideoOutputWindow? window, Task finished, Action complete, CancellationToken ct)
    {
        // Complete explicitly too: if the token is already cancelled, Stop() runs before
        // Play() and VLC never raises Stopped.
        using var registration = ct.Register(() =>
        {
            player.Stop();
            complete();
        });
        try
        {
            await finished;
        }
        finally
        {
            // Only unregister if a later GO of this same cue hasn't already replaced us.
            if (_activePlayers.TryGetValue(cue.Id, out var current) && current == player)
            {
                _activePlayers.Remove(cue.Id);
                cue.RemainingTime = null;
            }

            if (window is not null)
            {
                if (_activeWindows.TryGetValue(cue.Id, out var currentWindow) && currentWindow == window)
                {
                    _activeWindows.Remove(cue.Id);
                    VideoWindowClosed?.Invoke(cue.Id);
                }
                window.Detach();
                window.Close();
            }

            player.Dispose();
            media.Dispose();
        }
    }

    private static int ToVlcVolume(double volume01) => (int)Math.Clamp(volume01 * 100, 0, 100);
}
