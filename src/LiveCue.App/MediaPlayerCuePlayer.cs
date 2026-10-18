using System.ComponentModel;
using System.Globalization;
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

    /// <summary>How often a Fade cue updates its target's live volume/opacity, and an Image cue's countdown, while running.</summary>
    private static readonly TimeSpan AnimationTickInterval = TimeSpan.FromMilliseconds(40);

    /// <summary>
    /// The audio output module used for per-cue device routing (MediaCue.AudioOutputDeviceId) -
    /// the modern WASAPI-based Windows output, picked because (unlike the legacy directsound/
    /// waveout modules) it reliably enumerates real per-device descriptions.
    /// </summary>
    private const string AudioOutputModule = "mmdevice";

    private readonly LibVLC _libVlc;
    private readonly Func<string?, ScreenBounds?> _resolveBounds;
    private readonly CueList _cueList;
    private readonly Dictionary<Guid, MediaPlayer> _activePlayers = new();
    private readonly Dictionary<Guid, VideoOutputWindow> _activeWindows = new();
    private readonly Dictionary<Guid, ImageOutputWindow> _activeImageWindows = new();

    /// <summary>
    /// Per-image-cue cancellation, linked to whatever chain's token started it. Unlike a
    /// MediaCue, an Image cue has no MediaPlayer/Stopped event to unblock PlayImageAsync's wait
    /// when it's stopped - this is what Stop(Cue) cancels so a targeted Stop cue (PlayStopAsync)
    /// doesn't leave that image cue's PlayAsync task, and any "Continua" chain awaiting it,
    /// pending forever.
    /// </summary>
    private readonly Dictionary<Guid, CancellationTokenSource> _imageStopSources = new();

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
        ImageCue imageCue => PlayImageAsync(imageCue, ct),
        StopCue stopCue => PlayStopAsync(stopCue),
        _ => Task.CompletedTask,
    };

    public void Stop(Cue cue)
    {
        if (_activePlayers.TryGetValue(cue.Id, out var player))
            _ = StopOffUiThreadAsync(player);
        // Cancelling (rather than closing the window here directly) runs PlayImageAsync's own
        // finally block, same as every other cancellation path - one single cleanup path instead
        // of two that both have to agree on dictionary/window state.
        if (_imageStopSources.TryGetValue(cue.Id, out var stopSource))
            stopSource.Cancel();
    }

    public void StopAll()
    {
        // Each player's Stopped event then runs that cue's own cleanup (closing its window,
        // disposing the player) — even for cues whose GO chain has already finished.
        foreach (var player in _activePlayers.Values.ToList())
            _ = StopOffUiThreadAsync(player);

        // Hide every video/image output window immediately, rather than waiting for each cue's
        // own async cleanup to run — the panic button should clear the screen synchronously.
        foreach (var window in _activeWindows.Values.ToList())
            window.Hide();
        foreach (var window in _activeImageWindows.Values.ToList())
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
    /// Parses just enough of the file to learn its duration and basic technical info (bitrate,
    /// codec, resolution, sample rate - see MediaCue/VideoCue) for the cue list's "Durata" column
    /// and the inspector's "Informazioni" tab. Fire-and-forget from the caller; failures just
    /// leave those fields null.
    /// </summary>
    public async Task LoadMediaInfoAsync(Cue cue, string filePath)
    {
        if (!File.Exists(filePath)) return;
        try
        {
            using var media = new Media(_libVlc, new Uri(filePath));
            var status = await media.Parse(MediaParseOptions.ParseLocal);
            if (status != MediaParsedStatus.Done) return;

            if (media.Duration > 0)
                cue.Duration = TimeSpan.FromMilliseconds(media.Duration);

            if (cue is not MediaCue mediaCue) return;

            var videoTrack = media.Tracks.Where(t => t.TrackType == TrackType.Video).Select(t => (MediaTrack?)t).FirstOrDefault();
            var audioTrack = media.Tracks.Where(t => t.TrackType == TrackType.Audio).Select(t => (MediaTrack?)t).FirstOrDefault();

            if ((videoTrack ?? audioTrack) is { } primary)
            {
                mediaCue.Bitrate = primary.Bitrate > 0 ? primary.Bitrate : null;
                mediaCue.Codec = FourCcToString(primary.Codec);
            }
            if (audioTrack is { } audio)
            {
                mediaCue.AudioChannels = audio.Data.Audio.Channels;
                mediaCue.AudioSampleRate = audio.Data.Audio.Rate;
            }
            if (cue is VideoCue videoCue && videoTrack is { } video)
            {
                videoCue.VideoWidth = video.Data.Video.Width;
                videoCue.VideoHeight = video.Data.Video.Height;
                if (video.Data.Video.FrameRateDen > 0)
                    videoCue.FrameRate = (double)video.Data.Video.FrameRateNum / video.Data.Video.FrameRateDen;
            }
        }
        catch (Exception ex)
        {
            App.LogError(ex); // this info is purely informational; never fail because of it
        }
    }

    /// <summary>Decodes a packed FourCC (e.g. media track Codec) into its 4-letter code, e.g. "avc1", "mp4a".</summary>
    private static string? FourCcToString(uint fourCc)
    {
        if (fourCc == 0) return null;
        var chars = BitConverter.GetBytes(fourCc).Select(b => b is >= 32 and < 127 ? (char)b : '?').ToArray();
        return new string(chars);
    }

    /// <summary>
    /// Lists the audio output devices available for per-cue routing (MediaCue.AudioOutputDeviceId),
    /// always with a leading "system default" entry (AudioOutputDeviceId left null - no
    /// SetOutputDevice call at all, so unrelated cues are never affected by this list existing).
    /// Creates a throwaway MediaPlayer purely to query it; meant to be called once at startup.
    /// </summary>
    public IReadOnlyList<(string? Id, string Name)> EnumerateAudioOutputDevices()
    {
        var devices = new List<(string? Id, string Name)> { (null, "Predefinito di sistema") };
        try
        {
            using var player = new MediaPlayer(_libVlc);
            player.SetAudioOutput(AudioOutputModule);
            foreach (var device in player.AudioOutputDeviceEnum)
                devices.Add((device.DeviceIdentifier, device.Description));
        }
        catch (Exception ex)
        {
            App.LogError(ex); // the device list is a convenience; a failure just means "default only"
        }
        return devices;
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
        // Mark in/out: InvariantCulture, not the UI's own culture - an it-IT "2,5" would parse as
        // two separate VLC options ("start-time=2" and a stray "5") and silently misbehave.
        if (cue.TrimStart is { } trimStart)
            media.AddOption($":start-time={trimStart.TotalSeconds.ToString(CultureInfo.InvariantCulture)}");
        if (cue.TrimEnd is { } trimEnd)
            media.AddOption($":stop-time={trimEnd.TotalSeconds.ToString(CultureInfo.InvariantCulture)}");

        var player = new MediaPlayer(media) { Volume = ToVlcVolume(cue.Volume) };
        if (!string.IsNullOrEmpty(cue.AudioOutputDeviceId))
            player.SetOutputDevice(cue.AudioOutputDeviceId, AudioOutputModule);
        if (cue is VideoCue videoCueForAdjust)
            ApplyVideoAdjust(player, videoCueForAdjust);

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

        // Lets the "Volume" slider (and the inspector's color-correction sliders, for a video)
        // adjust a playing cue live, instead of only taking effect the next time it's fired.
        void OnCuePropertyChanged(object? s, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MediaCue.Volume))
                player.Volume = ToVlcVolume(cue.Volume);
            else if (cue is VideoCue liveVideoCue && e.PropertyName is nameof(VideoCue.Brightness)
                or nameof(VideoCue.Contrast) or nameof(VideoCue.Saturation))
                ApplyVideoAdjust(player, liveVideoCue);
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
    /// Shows a still image on its target screen. Has no MediaPlayer at all (just a WPF Image), so
    /// it's tracked in its own dictionary rather than <see cref="_activePlayers"/>. Completes (so
    /// a "Continua" chain can move on) after DisplaySeconds if set, or stays showing - and the
    /// returned task stays pending - until Stop()/PANIC/chain cancellation cancels <paramref name="ct"/>.
    /// </summary>
    private async Task PlayImageAsync(ImageCue cue, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(cue.FilePath) || !File.Exists(cue.FilePath))
        {
            CueFailed?.Invoke(cue, $"file non trovato: {cue.FilePath}");
            return;
        }

        var bounds = _resolveBounds(cue.TargetDisplayId);
        if (bounds is null)
        {
            CueFailed?.Invoke(cue, "schermo di uscita non trovato e nessun monitor secondario collegato");
            return;
        }

        ImageOutputWindow window;
        try
        {
            // Always a fresh window, same as video cues: a newer image cue on the same screen
            // just layers on top rather than replacing whatever's already showing there.
            window = new ImageOutputWindow(bounds, cue.FilePath);
        }
        catch (Exception ex)
        {
            App.LogError(ex);
            CueFailed?.Invoke(cue, $"immagine non valida: {Path.GetFileName(cue.FilePath)}");
            return;
        }

        // Linked to the chain's own ct, but also cancellable directly by Stop(Cue) - see the
        // field comment on _imageStopSources for why this cue type needs its own source.
        using var stopSource = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _imageStopSources[cue.Id] = stopSource;
        var imageCt = stopSource.Token;

        _activeImageWindows[cue.Id] = window;
        window.Show();

        try
        {
            if (cue.DisplaySeconds > 0)
            {
                var duration = TimeSpan.FromSeconds(cue.DisplaySeconds);
                var start = DateTime.UtcNow;
                while (true)
                {
                    var remaining = duration - (DateTime.UtcNow - start);
                    if (remaining <= TimeSpan.Zero) break;
                    cue.RemainingTime = remaining;
                    await Task.Delay(AnimationTickInterval, imageCt);
                }
            }
            else
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, imageCt); // stays up until stopped/cancelled
            }
        }
        catch (OperationCanceledException)
        {
            // Stop()/PANIC/chain cancellation: fall through to close the window below.
        }
        finally
        {
            cue.RemainingTime = null;
            if (_activeImageWindows.TryGetValue(cue.Id, out var current) && current == window)
                _activeImageWindows.Remove(cue.Id);
            if (_imageStopSources.TryGetValue(cue.Id, out var currentSource) && currentSource == stopSource)
                _imageStopSources.Remove(cue.Id);
            window.Close();
        }
    }

    /// <summary>
    /// Immediately stops another cue (by its Number), no fade - the audio/video/image "Stop" cue.
    /// Works for any currently-active cue since it just reuses the same <see cref="Stop(Cue)"/>
    /// every cue type is already stopped through (PANIC included).
    /// </summary>
    private Task PlayStopAsync(StopCue cue)
    {
        var target = _cueList.Cues.FirstOrDefault(c => c.Number == cue.TargetCueNumber);
        if (target is null)
        {
            CueFailed?.Invoke(cue, $"cue target \"{cue.TargetCueNumber}\" non trovata");
            return Task.CompletedTask;
        }
        if (!_activePlayers.ContainsKey(target.Id) && !_activeImageWindows.ContainsKey(target.Id))
        {
            CueFailed?.Invoke(cue, $"la cue target \"{cue.TargetCueNumber}\" non è in riproduzione");
            return Task.CompletedTask;
        }

        Stop(target);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Fades a currently-playing cue's volume (and, for a video, its picture) down to nothing over
    /// the Fade cue's duration, then stops it. The volume side reuses the live-volume wiring
    /// already in <see cref="PlayMediaAsync"/> (it reacts to Cue.Volume changes on its own) rather
    /// than touching the target's MediaPlayer directly. The picture side can't use the same trick:
    /// animating the video window's own Opacity would blend it with whatever is behind it on the
    /// desktop, not black, so a separate black <see cref="FadeOverlayWindow"/> is layered on top of
    /// it instead and that window's Opacity is what actually animates.
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

        FadeOverlayWindow? overlay = null;
        if (_activeWindows.TryGetValue(target.Id, out var targetWindow))
        {
            overlay = new FadeOverlayWindow(targetWindow.Bounds);
            overlay.Show();
        }

        var duration = TimeSpan.FromSeconds(Math.Max(0, cue.FadeSeconds));
        var startVolume = targetMedia.Volume;
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
                if (overlay is not null) overlay.Opacity = 1 - level;
                cue.RemainingTime = duration - elapsed;

                await Task.Delay(AnimationTickInterval, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // PANIC/Stop during the fade: fall through to the restore/cleanup below.
        }
        finally
        {
            cue.RemainingTime = null;
            if (overlay is not null) overlay.Opacity = 1; // fully black even if the loop above exited early

            // Stop (and wait for VLC's native video surface to actually tear down) BEFORE
            // restoring the volume and dropping the black overlay: PlayMediaAsync's own live-volume
            // handler is still subscribed at this point, so restoring first would push the still-
            // playing MediaPlayer straight back up to full volume for the moment it takes Stop() to
            // actually land - audible as a one-frame blip of the track "restarting" right at 0.
            if (!ct.IsCancellationRequested && _activePlayers.TryGetValue(target.Id, out var current) && current == targetPlayer)
                await StopOffUiThreadAsync(targetPlayer);

            targetMedia.Volume = startVolume; // restore the cue's own level for the next time it plays
            overlay?.Close();
        }
    }

    private static int ToVlcVolume(double volume01) => (int)Math.Clamp(volume01 * 100, 0, 100);

    /// <summary>Applies the inspector's basic color correction via LibVLC's "adjust" video filter.</summary>
    private static void ApplyVideoAdjust(MediaPlayer player, VideoCue cue)
    {
        player.SetAdjustInt(VideoAdjustOption.Enable, 1);
        player.SetAdjustFloat(VideoAdjustOption.Brightness, (float)cue.Brightness);
        player.SetAdjustFloat(VideoAdjustOption.Contrast, (float)cue.Contrast);
        player.SetAdjustFloat(VideoAdjustOption.Saturation, (float)cue.Saturation);
    }
}
