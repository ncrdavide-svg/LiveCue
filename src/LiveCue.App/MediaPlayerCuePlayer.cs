using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
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
    private readonly Func<string?, ResolvedOutputTarget?> _resolveTarget;
    private readonly CueList _cueList;
    /// <summary>Keeps a transparent overlay window (VirtualDisplay.IsTransparentOverlay) visually
    /// above every other output window on the same physical monitor - see OverlayZOrderTracker.</summary>
    private readonly OverlayZOrderTracker _overlayZOrder = new();
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

    /// <summary>
    /// Fired right after any cue's output window (video OR image) is shown - lets a monitor-
    /// preview window mirror it live via DWM, which only needs the window's handle and its
    /// on-screen bounds, not the window itself.
    /// </summary>
    public event Action<Guid, IntPtr, ScreenBounds>? OutputWindowOpened;

    /// <summary>Fired right before a cue's output window is closed.</summary>
    public event Action<Guid>? OutputWindowClosed;

    /// <summary>
    /// Fired when a cue can't be played (missing file, no output screen, VLC error). The cue is
    /// then treated as finished, so a "Continua" chain moves on instead of hanging.
    /// May be raised from a LibVLC thread.
    /// </summary>
    public event Action<Cue, string>? CueFailed;

    public MediaPlayerCuePlayer(LibVLC libVlc, Func<string?, ResolvedOutputTarget?> resolveTarget, CueList cueList)
    {
        _libVlc = libVlc;
        _resolveTarget = resolveTarget;
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

    private const int WaveformBuckets = 800;
    private const int ThumbnailCount = 12;
    private const int ThumbnailWidth = 160;

    /// <summary>
    /// Generates (or loads from disk cache - see MediaAnalysisCache) the In/Out tab scrubber's
    /// waveform and, for a video, preview thumbnails. Both decode the whole file, so this is slow
    /// (seconds) - meant to run fire-and-forget right after LoadMediaInfoAsync, never awaited by
    /// a caller that needs to stay responsive. Failures just leave the scrubber showing nothing.
    /// </summary>
    public async Task AnalyzeMediaAsync(Cue cue)
    {
        if (cue is not MediaCue mediaCue || string.IsNullOrWhiteSpace(mediaCue.FilePath) || !File.Exists(mediaCue.FilePath))
            return;

        mediaCue.IsAnalyzing = true;
        try
        {
            var cacheDir = MediaAnalysisCache.GetCacheDir(mediaCue.FilePath);
            var duration = mediaCue.Duration ?? TimeSpan.Zero;

            var cachedWaveform = MediaAnalysisCache.LoadWaveform(cacheDir);
            if (cachedWaveform is not null)
            {
                mediaCue.WaveformPeaks = cachedWaveform;
            }
            else
            {
                var generated = await GenerateWaveformAsync(mediaCue.FilePath, duration);
                mediaCue.WaveformPeaks = generated;
                if (generated is not null) MediaAnalysisCache.SaveWaveform(cacheDir, generated);
            }

            if (cue is VideoCue videoCue && duration > TimeSpan.Zero)
            {
                videoCue.ThumbnailPaths = MediaAnalysisCache.LoadThumbnails(cacheDir, ThumbnailCount)
                    ?? await GenerateThumbnailsAsync(mediaCue.FilePath, duration, videoCue, cacheDir);
            }
        }
        catch (Exception ex)
        {
            App.LogError(ex); // the scrubber preview is informational; never fail because of it
        }
        finally
        {
            mediaCue.IsAnalyzing = false;
        }
    }

    /// <summary>
    /// Decodes the file's audio through LibVLC's audio callbacks (not through a real output
    /// device - EndReached/Drain just signals "fully decoded") and reduces it to one peak
    /// amplitude per horizontal pixel bucket. Buckets are by cumulative sample count, not by the
    /// callback's own PTS, so the result doesn't depend on a particular demuxer's timestamp
    /// behavior. Returns null if the file has no audio track (Setup never runs, channels stays 0).
    /// </summary>
    private async Task<float[]?> GenerateWaveformAsync(string filePath, TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero) return null;

        var peaks = new float[WaveformBuckets];
        uint channels = 0;
        long framesPerBucket = 0;
        long framesSeen = 0;

        int Setup(ref IntPtr opaque, ref IntPtr format, ref uint rate, ref uint chans)
        {
            // Deliberately NOT writing to `format`: it's a native char[4] buffer (the fourcc,
            // e.g. "S16N") that LibVLCSharp's binding marshals as a bare IntPtr rather than a
            // byte*, and writing through it crashed with an AccessViolationException - confirmed
            // live (first version of this method). Leaving it untouched accepts libvlc's own
            // proposed default, which is S16 (signed 16-bit) in every build this has been tested
            // against; only rate/channels (plain ref uint, no such mismatch) are overridden below,
            // to a format this method's own sample-reading code (16-bit, see Play below) matches.
            rate = 44100;
            chans = 2;
            channels = chans;
            framesPerBucket = Math.Max(1, (long)(duration.TotalSeconds * rate) / WaveformBuckets);
            return 0;
        }
        void Cleanup(IntPtr opaque) { }
        void Pause(IntPtr data, long pts) { }
        void Resume(IntPtr data, long pts) { }
        void Flush(IntPtr data, long pts) { }
        void Play(IntPtr data, IntPtr samples, uint count, long pts)
        {
            if (channels == 0 || framesPerBucket == 0) return;
            var frameCount = (int)count;
            var buffer = new short[frameCount * (int)channels];
            Marshal.Copy(samples, buffer, 0, buffer.Length);
            for (var f = 0; f < frameCount; f++)
            {
                float peak = 0;
                for (var ch = 0; ch < channels; ch++)
                {
                    var v = Math.Abs(buffer[f * channels + ch] / 32768f);
                    if (v > peak) peak = v;
                }
                var bucket = (int)((framesSeen + f) / framesPerBucket);
                if (bucket >= 0 && bucket < WaveformBuckets && peak > peaks[bucket])
                    peaks[bucket] = peak;
            }
            framesSeen += frameCount;
        }

        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Drain(IntPtr data) => done.TrySetResult();

        // Delegates passed to native code (SetAudioFormatCallback/SetAudioCallbacks below) are
        // NOT kept alive by that call alone - LibVLCSharp hands libvlc a raw function pointer and
        // nothing here references the delegate OBJECTS again afterwards, so the GC is free to
        // collect them while libvlc's own audio thread is still calling back into them. That is
        // exactly what happened the first time this ran: an AccessViolationException a few
        // seconds in, from libvlc invoking an already-collected callback. Explicit locals +
        // GC.KeepAlive after Stop() has fully returned (so no further callback can fire) is the
        // standard fix for this category of P/Invoke bug.
        MediaPlayer.LibVLCAudioSetupCb setupCb = Setup;
        MediaPlayer.LibVLCAudioCleanupCb cleanupCb = Cleanup;
        MediaPlayer.LibVLCAudioPlayCb playCb = Play;
        MediaPlayer.LibVLCAudioPauseCb pauseCb = Pause;
        MediaPlayer.LibVLCAudioResumeCb resumeCb = Resume;
        MediaPlayer.LibVLCAudioFlushCb flushCb = Flush;
        MediaPlayer.LibVLCAudioDrainCb drainCb = Drain;

        using var media = new Media(_libVlc, new Uri(filePath));
        media.AddOption(":no-video"); // audio only - no output window needed, nothing else to decode
        using var player = new MediaPlayer(media);
        player.EndReached += (_, _) => done.TrySetResult();
        player.EncounteredError += (_, _) => done.TrySetResult();
        player.SetAudioFormatCallback(setupCb, cleanupCb);
        player.SetAudioCallbacks(playCb, pauseCb, resumeCb, flushCb, drainCb);

        // No SetRate() speed-up here: confirmed live that changing the playback rate on a
        // callback-only (no real output device) player makes libvlc drain almost immediately
        // without ever invoking Play - zero samples captured, silently. Decoding this way runs at
        // roughly real-time instead, which is why AnalyzeMediaAsync's caller treats this as a
        // slow, fire-and-forget, disk-cached background task rather than something to await.
        player.Play();

        var timeoutSeconds = Math.Max(20, duration.TotalSeconds * 2);
        await Task.WhenAny(done.Task, Task.Delay(TimeSpan.FromSeconds(timeoutSeconds)));

        // Same reason as StopOffUiThreadAsync elsewhere in this class: Stop() blocks until libvlc
        // tears down, never on the UI thread. This player never had a window, but Stop() itself
        // is still the same blocking native call.
        await Task.Run(() =>
        {
            try { player.Stop(); }
            catch (Exception ex) { App.LogError(ex); }
        });

        // Only safe to let these go once Stop() above has fully returned - see the comment where
        // they're declared.
        GC.KeepAlive(setupCb);
        GC.KeepAlive(cleanupCb);
        GC.KeepAlive(playCb);
        GC.KeepAlive(pauseCb);
        GC.KeepAlive(resumeCb);
        GC.KeepAlive(flushCb);
        GC.KeepAlive(drainCb);

        return channels == 0 ? null : peaks;
    }

    /// <summary>
    /// Captures <see cref="ThumbnailCount"/> evenly-spaced preview frames via a hidden,
    /// off-screen player (TakeSnapshot needs an active video output, but it never has to be
    /// visible). Snapshots are taken opportunistically as a single normal playthrough crosses
    /// each target timestamp, rather than pausing and seeking to each one individually - tried
    /// that first, and confirmed live it's unreliable (TakeSnapshot after a paused seek mostly
    /// failed silently; only the very last of 12 thumbnails ever actually appeared). Snapshotting
    /// while actively playing is libvlc's ordinary, well-trodden use of TakeSnapshot.
    /// </summary>
    private async Task<IReadOnlyList<string>?> GenerateThumbnailsAsync(string filePath, TimeSpan duration, VideoCue cue, string cacheDir)
    {
        var window = new ThumbnailCaptureWindow();
        try
        {
            window.Show();
            using var media = new Media(_libVlc, new Uri(filePath));
            media.AddOption(":no-audio");
            using var player = new MediaPlayer(media);
            window.Attach(player);

            var height = cue.VideoWidth is > 0 && cue.VideoHeight is > 0
                ? (uint)Math.Round(ThumbnailWidth * (double)cue.VideoHeight.Value / cue.VideoWidth.Value)
                : (uint)(ThumbnailWidth * 9 / 16);

            var targetsMs = Enumerable.Range(0, ThumbnailCount)
                .Select(i => (long)(duration.TotalMilliseconds * (i + 0.5) / ThumbnailCount))
                .ToArray();
            var paths = new string?[ThumbnailCount];
            var nextIndex = 0;
            var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            void OnTimeChanged(object? s, MediaPlayerTimeChangedEventArgs e)
            {
                // A single TimeChanged tick can cross more than one target if playback briefly
                // stalls (buffering) - catch up with a loop rather than only ever taking one.
                while (nextIndex < targetsMs.Length && e.Time >= targetsMs[nextIndex])
                {
                    var snapshotPath = MediaAnalysisCache.ThumbnailFile(cacheDir, nextIndex);
                    if (player.TakeSnapshot(0, snapshotPath, (uint)ThumbnailWidth, height))
                        paths[nextIndex] = snapshotPath;
                    nextIndex++;
                }
                if (nextIndex >= targetsMs.Length) done.TrySetResult();
            }
            player.TimeChanged += OnTimeChanged;
            player.EndReached += (_, _) => done.TrySetResult();
            player.EncounteredError += (_, _) => done.TrySetResult();

            player.Play();
            await Task.WhenAny(done.Task, Task.Delay(TimeSpan.FromSeconds(Math.Max(20, duration.TotalSeconds * 2))));
            player.TimeChanged -= OnTimeChanged;

            await Task.Run(() =>
            {
                try { player.Stop(); }
                catch (Exception ex) { App.LogError(ex); }
            });

            // TakeSnapshot writes the file asynchronously even after it returns true - give the
            // last couple a moment to actually land before checking what's there.
            for (var wait = 0; wait < 20 && paths.Any(p => p is not null && !File.Exists(p)); wait++)
                await Task.Delay(100);

            var existing = paths.Where(p => p is not null && File.Exists(p)).Select(p => p!).ToList();
            return existing.Count > 0 ? existing : null;
        }
        finally
        {
            window.Close();
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
        string? hostDeviceName = null;
        if (cue is VideoCue videoCue)
        {
            var target = _resolveTarget(videoCue.TargetDisplayId);
            if (target is null)
            {
                CueFailed?.Invoke(cue, "schermo di uscita non trovato e nessun monitor secondario collegato");
                return Task.CompletedTask;
            }
            // Always a fresh window, even if another cue is already showing on the same screen:
            // it becomes a new layer on top, the earlier cue keeps playing untouched underneath.
            // Never transparent even on a transparent-overlay VirtualDisplay: LibVLC's video
            // surface is a native child window, which a WPF layered (AllowsTransparency) window
            // does not composite at all - see VirtualDisplay.IsTransparentOverlay.
            window = new VideoOutputWindow(target.Bounds);
            window.ApplyTransform(videoCue.Zoom, videoCue.OffsetX, videoCue.OffsetY, videoCue.Rotation);
            hostDeviceName = target.HostDeviceName;
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

            // player.Length always reports the whole file's length, even with a mark out
            // (:stop-time) set - a trimmed cue's own end is whichever comes first.
            var length = player.Length;
            if (length <= 0) return;
            var effectiveEndMs = cue.TrimEnd is { } trimEnd ? Math.Min(length, trimEnd.TotalMilliseconds) : length;
            var remaining = TimeSpan.FromMilliseconds(Math.Max(0, effectiveEndMs - args.Time));
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
            else if (window is not null && cue is VideoCue transformCue && e.PropertyName is nameof(VideoCue.Zoom)
                or nameof(VideoCue.OffsetX) or nameof(VideoCue.OffsetY) or nameof(VideoCue.Rotation))
                window.ApplyTransform(transformCue.Zoom, transformCue.OffsetX, transformCue.OffsetY, transformCue.Rotation);
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
            OutputWindowOpened?.Invoke(cue.Id, window.Handle, window.Bounds);
            // A video window is never itself a transparent overlay (see above) - only relevant
            // here if an overlay for this same monitor is already open and needs to stay on top.
            _overlayZOrder.WindowOpened(hostDeviceName, window.Handle, isOverlay: false);
        }

        cue.IsPlaying = true;
        _ = RunUntilFinishedAsync(cue, player, media, window, hostDeviceName, finished.Task, Complete, OnCuePropertyChanged, ct);
        player.Play();
        return firstPassDone.Task;
    }

    /// <summary>Owns the player for its whole life (which, for a loop, outlasts the first pass) and cleans up at the end.</summary>
    private async Task RunUntilFinishedAsync(MediaCue cue, MediaPlayer player, Media media, VideoOutputWindow? window,
        string? hostDeviceName, Task finished, Action complete, PropertyChangedEventHandler onCuePropertyChanged, CancellationToken ct)
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
                cue.IsPlaying = false;
            }

            if (window is not null && _activeWindows.TryGetValue(cue.Id, out var currentWindow) && currentWindow == window)
            {
                _activeWindows.Remove(cue.Id);
                OutputWindowClosed?.Invoke(cue.Id);
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

        var target = _resolveTarget(cue.TargetDisplayId);
        if (target is null)
        {
            CueFailed?.Invoke(cue, "schermo di uscita non trovato e nessun monitor secondario collegato");
            return;
        }

        ImageOutputWindow window;
        try
        {
            // Always a fresh window, same as video cues: a newer image cue on the same screen
            // just layers on top rather than replacing whatever's already showing there.
            window = new ImageOutputWindow(target.Bounds, cue.FilePath, target.IsTransparentOverlay);
            window.ApplyTransform(cue.Zoom, cue.OffsetX, cue.OffsetY, cue.Rotation);
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

        // Lets the inspector's pan/zoom/rotate sliders reposition a cue already on screen,
        // same idea as a playing video's own live color-correction/volume updates.
        void OnCuePropertyChanged(object? s, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(ImageCue.Zoom) or nameof(ImageCue.OffsetX)
                or nameof(ImageCue.OffsetY) or nameof(ImageCue.Rotation))
                window.ApplyTransform(cue.Zoom, cue.OffsetX, cue.OffsetY, cue.Rotation);
        }
        cue.PropertyChanged += OnCuePropertyChanged;

        _activeImageWindows[cue.Id] = window;
        window.Show();
        OutputWindowOpened?.Invoke(cue.Id, window.Handle, window.Bounds);
        _overlayZOrder.WindowOpened(target.HostDeviceName, window.Handle, target.IsTransparentOverlay);
        cue.IsPlaying = true;

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
            cue.PropertyChanged -= OnCuePropertyChanged;
            cue.RemainingTime = null;
            cue.IsPlaying = false;
            if (_activeImageWindows.TryGetValue(cue.Id, out var current) && current == window)
            {
                _activeImageWindows.Remove(cue.Id);
                OutputWindowClosed?.Invoke(cue.Id);
            }
            _overlayZOrder.WindowClosed(target.HostDeviceName, window.Handle);
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
        cue.IsPlaying = true;

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
            cue.IsPlaying = false;
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
