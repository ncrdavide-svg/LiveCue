using System.ComponentModel;
using System.IO;
using System.Linq;
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

    /// <summary>How often a Fade cue updates its target's live volume/window opacity while fading.</summary>
    private static readonly TimeSpan FadeTickInterval = TimeSpan.FromMilliseconds(40);

    private readonly LibVLC _libVlc;
    private readonly Func<string?, ScreenBounds?> _resolveBounds;
    private readonly CueList _cueList;
    private readonly Dictionary<Guid, MediaPlayer> _activePlayers = new();
    private readonly Dictionary<Guid, VideoOutputWindow> _activeWindows = new();

    /// <summary>Fired right after a cue's video output window is shown — lets a monitor-preview window mirror it live.</summary>
    public event Action<Guid, VideoOutputWindow>? VideoWindowOpened;

    /// <summary>Fired right before a cue's video output window is closed.</summary>
    public event Action<Guid>? VideoWindowClosed;

    /// <summary>
    /// Fired when a cue can't be played (missing file, no output screen, VLC error). The cue is
    /// then treated as finished, so a "Continua" chain moves on instead of hanging.
    /// May be raised from a LibVLC thread.
    /// </summary>
    public event Action<Cue, string>? CueFailed;

    public MediaPlayerCuePlayer(LibVLC libVlc, Func<string?, ScreenBounds?> resolveBounds, CueList cueList)
    {
        _libVlc = libVlc;
        _resolveBounds = resolveBounds;
        _cueList = cueList;
    }

    public Task PlayAsync(Cue cue, CancellationToken ct) => cue switch
    {
        MediaCue mediaCue => PlayMediaAsync(mediaCue, ct),
        FadeCue fadeCue => PlayFadeAsync(fadeCue, ct),
        _ => Task.CompletedTask,
    };

    public void Stop(Cue cue)
    {
        if (_activePlayers.TryGetValue(cue.Id, out var player))
            _ = StopOffUiThreadAsync(player);
    }

    public void StopAll()
    {
        // Each player's Stopped event then runs that cue's own cleanup (closing its window,
        // disposing the player) — even for cues whose GO chain has already finished.
        foreach (var player in _activePlayers.Values.ToList())
            _ = StopOffUiThreadAsync(player);

        // Hide every video output window immediately, rather than waiting for each cue's own
        // async cleanup to run — the panic button should clear the screen synchronously.
        foreach (var window in _activeWindows.Values.ToList())
            window.Hide();
    }

    // Stop tasks per player, so PANIC + chain cancellation + cleanup never stop one player twice.
    // Only touched from the UI thread.
    private readonly Dictionary<MediaPlayer, Task> _stopTasks = new();

    /// <summary>
    /// MediaPlayer.Stop() (and Dispose() of a player that still has its video output) BLOCK until
    /// VLC has torn down its video output — and VLC's output thread, while doing that, sends
    /// messages to the video window, which belongs to the UI thread. Calling them on the UI
    /// thread is therefore a deadlock: reproduced with two fullscreen videos on two monitors +
    /// PANIC (app froze, then Windows offered to close it). They always run on a worker thread.
    /// </summary>
    private Task StopOffUiThreadAsync(MediaPlayer player)
    {
        if (_stopTasks.TryGetValue(player, out var running)) return running;

        var task = Task.Run(() =>
        {
            try { player.Stop(); }
            catch (Exception ex) { App.LogError(ex); }
        });
        _stopTasks[player] = task;
        return task;
    }

    /// <summary>
    /// Parses just enough of the file to learn its duration and stores it on the cue, for the
    /// cue list's "Durata" column. Fire-and-forget from the caller; failures just leave it null.
    /// </summary>
    public async Task LoadDurationAsync(Cue cue, string filePath)
    {
        if (!File.Exists(filePath)) return;
        try
        {
            using var media = new Media(_libVlc, new Uri(filePath));
            var status = await media.Parse(MediaParseOptions.ParseLocal);
            if (status == MediaParsedStatus.Done && media.Duration > 0)
                cue.Duration = TimeSpan.FromMilliseconds(media.Duration);
        }
        catch (Exception ex)
        {
            App.LogError(ex); // the duration is informational; never fail because of it
        }
    }

    /// <returns>A task that completes at the end of the first pass (see <see cref="ICuePlayer.PlayAsync"/>).</returns>
    private Task PlayMediaAsync(MediaCue cue, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(cue.FilePath) || !File.Exists(cue.FilePath))
        {
            CueFailed?.Invoke(cue, $"file non trovato: {cue.FilePath}");
            return Task.CompletedTask;
        }

        VideoOutputWindow? window = null;
        if (cue is VideoCue videoCue)
        {
            var bounds = _resolveBounds(videoCue.TargetDisplayId);
            if (bounds is null)
            {
                CueFailed?.Invoke(cue, "schermo di uscita non trovato e nessun monitor secondario collegato");
                return Task.CompletedTask;
            }
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
        // Unreadable/corrupt media: VLC raises neither of the above. Without this the output
        // window would stay black on screen and a "Continua" chain would hang forever.
        player.EncounteredError += (_, _) =>
        {
            CueFailed?.Invoke(cue, $"VLC non riesce a riprodurre {Path.GetFileName(cue.FilePath)}");
            Complete();
        };

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

        // Lets the "Volume" slider in the cue list fade a playing cue live, instead of only
        // taking effect the next time it's fired.
        void OnCuePropertyChanged(object? s, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MediaCue.Volume))
                player.Volume = ToVlcVolume(cue.Volume);
        }
        cue.PropertyChanged += OnCuePropertyChanged;

        _activePlayers[cue.Id] = player;
        if (window is not null)
        {
            window.Attach(player);
            // No Activate(): the window must never take keyboard focus from the main window, or
            // the operator gets locked out (can't click past a topmost video, can't PANIC either
            // once it has focus) if a cue's screen happens to be the one the app is on.
            // New Topmost windows already layer above older Topmost ones without needing it.
            window.Show();
            _activeWindows[cue.Id] = window;
            VideoWindowOpened?.Invoke(cue.Id, window);
        }

        _ = RunUntilFinishedAsync(cue, player, media, window, finished.Task, Complete, OnCuePropertyChanged, ct);
        player.Play();
        return firstPassDone.Task;
    }

    /// <summary>Owns the player for its whole life (which, for a loop, outlasts the first pass) and cleans up at the end.</summary>
    private async Task RunUntilFinishedAsync(MediaCue cue, MediaPlayer player, Media media, VideoOutputWindow? window,
        Task finished, Action complete, PropertyChangedEventHandler onCuePropertyChanged, CancellationToken ct)
    {
        // Complete explicitly too: if the token is already cancelled, Stop() runs before
        // Play() and VLC never raises Stopped.
        using var registration = ct.Register(() =>
        {
            _ = StopOffUiThreadAsync(player);
            complete();
        });
        try
        {
            await finished;
        }
        finally
        {
            cue.PropertyChanged -= onCuePropertyChanged;

            // Only unregister if a later GO of this same cue hasn't already replaced us.
            if (_activePlayers.TryGetValue(cue.Id, out var current) && current == player)
            {
                _activePlayers.Remove(cue.Id);
                cue.RemainingTime = null;
            }

            if (window is not null && _activeWindows.TryGetValue(cue.Id, out var currentWindow) && currentWindow == window)
            {
                _activeWindows.Remove(cue.Id);
                VideoWindowClosed?.Invoke(cue.Id);
            }
            window?.Hide();

            // Order matters: VLC must have fully released its video output (worker thread, see
            // StopOffUiThreadAsync) before the player is detached/disposed and, last, before the
            // window that hosts VLC's native video surface is destroyed.
            await StopOffUiThreadAsync(player);
            window?.Detach();
            await Task.Run(() =>
            {
                try
                {
                    player.Dispose();
                    media.Dispose();
                }
                catch (Exception ex)
                {
                    App.LogError(ex);
                }
            });
            _stopTasks.Remove(player);
            window?.Close();
        }
    }

    /// <summary>
    /// Fades a currently-playing cue's volume (and, for a video, its window's opacity) down to
    /// nothing over the Fade cue's duration, then stops it. Reuses the live-volume wiring already
    /// in <see cref="PlayMediaAsync"/> (it reacts to Cue.Volume changes on its own) rather than
    /// touching the target's MediaPlayer directly, so a fade is just an animation of the model.
    /// </summary>
    private async Task PlayFadeAsync(FadeCue cue, CancellationToken ct)
    {
        var target = _cueList.Cues.FirstOrDefault(c => c.Number == cue.TargetCueNumber);
        if (target is null)
        {
            CueFailed?.Invoke(cue, $"cue target \"{cue.TargetCueNumber}\" non trovata");
            return;
        }
        if (target is not MediaCue targetMedia)
        {
            CueFailed?.Invoke(cue, $"la cue target \"{cue.TargetCueNumber}\" non è audio/video");
            return;
        }
        if (!_activePlayers.TryGetValue(target.Id, out var targetPlayer))
        {
            CueFailed?.Invoke(cue, $"la cue target \"{cue.TargetCueNumber}\" non è in riproduzione");
            return;
        }

        _activeWindows.TryGetValue(target.Id, out var targetWindow);

        var duration = TimeSpan.FromSeconds(Math.Max(0, cue.FadeSeconds));
        var startVolume = targetMedia.Volume;
        var startOpacity = targetWindow?.Opacity ?? 1.0;
        var start = DateTime.UtcNow;

        try
        {
            while (!ct.IsCancellationRequested)
            {
                // The target may have ended, been stopped, or been re-fired independently while
                // we were fading it - either way there's nothing left of this playback to fade.
                if (!_activePlayers.TryGetValue(target.Id, out var current) || current != targetPlayer)
                    return;

                var elapsed = DateTime.UtcNow - start;
                if (elapsed >= duration) break;

                var level = duration > TimeSpan.Zero ? 1 - elapsed.TotalSeconds / duration.TotalSeconds : 0;
                targetMedia.Volume = startVolume * level;
                if (_activeWindows.TryGetValue(target.Id, out var currentWindow) && currentWindow == targetWindow)
                    currentWindow.Opacity = startOpacity * level;
                cue.RemainingTime = duration - elapsed;

                await Task.Delay(FadeTickInterval, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // PANIC/Stop during the fade: fall through to the restore/cleanup below.
        }
        finally
        {
            cue.RemainingTime = null;
            targetMedia.Volume = startVolume; // restore the cue's own level for the next time it plays

            if (!ct.IsCancellationRequested && _activePlayers.TryGetValue(target.Id, out var current) && current == targetPlayer)
                Stop(target); // fully silent/invisible now - stop it rather than leaving it stuck at zero
        }
    }

    private static int ToVlcVolume(double volume01) => (int)Math.Clamp(volume01 * 100, 0, 100);
}
