using LiveCue.Core.Models;
using LiveCue.Core.Services;

namespace LiveCue.Core.Tests;

public sealed class ProjectFileTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "LiveCueTests-" + Guid.NewGuid());

    public ProjectFileTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void SaveLoad_RoundTripsCuesAndVirtualDisplays_KeepingTargetLinked()
    {
        var virtualDisplay = new VirtualDisplay { Name = "Palco", HostDeviceName = @"\\.\DISPLAY2", OffsetX = 10, OffsetY = 20, Width = 1280, Height = 720 };
        var list = new CueList();
        var video = new VideoCue { Number = "1", Name = "Intro", FilePath = Path.Combine(_dir, "intro.mp4"), TargetDisplayId = virtualDisplay.Id, Loop = true };
        var audio = new AudioCue { Number = "2", FilePath = Path.Combine(_dir, "music.wav"), Volume = 0.5, PreWait = TimeSpan.FromSeconds(2) };
        list.Cues.Add(video);
        list.Cues.Add(audio);
        var projectPath = Path.Combine(_dir, "show.livecue");

        ProjectFile.Save(list, new[] { virtualDisplay }, projectPath);

        var loaded = new CueList();
        var loadedDisplays = new List<VirtualDisplay>();
        ProjectFile.Load(loaded, loadedDisplays, projectPath);

        var loadedDisplay = Assert.Single(loadedDisplays);
        Assert.Equal(virtualDisplay.Id, loadedDisplay.Id);
        Assert.Equal((10, 20, 1280, 720), (loadedDisplay.OffsetX, loadedDisplay.OffsetY, loadedDisplay.Width, loadedDisplay.Height));

        var loadedVideo = Assert.IsType<VideoCue>(loaded.Cues[0]);
        Assert.Equal(video.Id, loadedVideo.Id);
        Assert.Equal(loadedDisplay.Id, loadedVideo.TargetDisplayId);
        Assert.True(loadedVideo.Loop);
        Assert.Equal(video.FilePath, loadedVideo.FilePath);

        var loadedAudio = Assert.IsType<AudioCue>(loaded.Cues[1]);
        Assert.Equal(0.5, loadedAudio.Volume);
        Assert.Equal(TimeSpan.FromSeconds(2), loadedAudio.PreWait);
    }

    [Fact]
    public void SaveLoad_RoundTripsFadeCue()
    {
        var list = new CueList();
        list.Cues.Add(new AudioCue { Number = "1", FilePath = Path.Combine(_dir, "music.wav") });
        list.Cues.Add(new FadeCue { Number = "2", Name = "Fade out", TargetCueNumber = "1", FadeSeconds = 3.5 });
        var projectPath = Path.Combine(_dir, "show.livecue");

        ProjectFile.Save(list, Array.Empty<VirtualDisplay>(), projectPath);

        var loaded = new CueList();
        ProjectFile.Load(loaded, new List<VirtualDisplay>(), projectPath);

        var loadedFade = Assert.IsType<FadeCue>(loaded.Cues[1]);
        Assert.Equal("1", loadedFade.TargetCueNumber);
        Assert.Equal(3.5, loadedFade.FadeSeconds);
    }

    [Fact]
    public void SaveLoad_RoundTripsImageCue_WithRelativePath()
    {
        var list = new CueList();
        list.Cues.Add(new ImageCue { Number = "1", Name = "Logo", FilePath = Path.Combine(_dir, "logo.png"), DisplaySeconds = 5 });
        var projectPath = Path.Combine(_dir, "show.livecue");

        ProjectFile.Save(list, Array.Empty<VirtualDisplay>(), projectPath);

        var json = File.ReadAllText(projectPath);
        Assert.Contains("logo.png", json);
        Assert.DoesNotContain(_dir.Replace(@"\", @"\\"), json);

        var loaded = new CueList();
        ProjectFile.Load(loaded, new List<VirtualDisplay>(), projectPath);

        var loadedImage = Assert.IsType<ImageCue>(loaded.Cues[0]);
        Assert.Equal(Path.Combine(_dir, "logo.png"), loadedImage.FilePath);
        Assert.Equal(5, loadedImage.DisplaySeconds);
    }

    [Fact]
    public void Load_KeepsContinueModes()
    {
        var list = new CueList();
        list.Cues.Add(new AudioCue { FilePath = Path.Combine(_dir, "1.wav") });
        list.Cues.Add(new AudioCue { FilePath = Path.Combine(_dir, "2.wav") });
        list.Cues.Add(new AudioCue { FilePath = Path.Combine(_dir, "3.wav") });
        list.Cues[0].ContinueMode = ContinueMode.Continue;
        list.Cues[1].ContinueMode = ContinueMode.StartTogether;
        var projectPath = Path.Combine(_dir, "show.livecue");
        ProjectFile.Save(list, Array.Empty<VirtualDisplay>(), projectPath);

        var loaded = new CueList();
        ProjectFile.Load(loaded, new List<VirtualDisplay>(), projectPath);

        Assert.Equal(
            new[] { ContinueMode.Continue, ContinueMode.StartTogether, ContinueMode.DoNotContinue },
            loaded.Cues.Select(c => c.ContinueMode));
    }

    [Fact]
    public void Save_StoresMediaPathsRelativeToProject_WithoutTouchingCuesInMemory()
    {
        var mediaPath = Path.Combine(_dir, "media", "intro.mp4");
        var list = new CueList();
        list.Cues.Add(new VideoCue { FilePath = mediaPath });
        var projectPath = Path.Combine(_dir, "show.livecue");

        ProjectFile.Save(list, Array.Empty<VirtualDisplay>(), projectPath);

        var json = File.ReadAllText(projectPath);
        Assert.Contains(@"media\\intro.mp4", json);
        Assert.DoesNotContain(_dir.Replace(@"\", @"\\"), json);
        Assert.Equal(mediaPath, ((VideoCue)list.Cues[0]).FilePath);
    }

    [Fact]
    public void MovedShowFolder_StillFindsItsMedia()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "a"));
        var list = new CueList();
        list.Cues.Add(new AudioCue { FilePath = Path.Combine(_dir, "a", "media", "song.wav") });
        ProjectFile.Save(list, Array.Empty<VirtualDisplay>(), Path.Combine(_dir, "a", "show.livecue"));

        Directory.Move(Path.Combine(_dir, "a"), Path.Combine(_dir, "b"));

        var loaded = new CueList();
        ProjectFile.Load(loaded, new List<VirtualDisplay>(), Path.Combine(_dir, "b", "show.livecue"));
        Assert.Equal(Path.Combine(_dir, "b", "media", "song.wav"), ((AudioCue)loaded.Cues[0]).FilePath);
    }

    [Fact]
    public void OldProjectWithMissingAbsolutePath_FallsBackToFileBesideProject()
    {
        File.WriteAllText(Path.Combine(_dir, "song.wav"), "");

        var resolved = ProjectFile.ResolveStoredPath(@"Z:\does\not\exist\song.wav", _dir);

        Assert.Equal(Path.Combine(_dir, "song.wav"), resolved);
    }

    [Fact]
    public void OldProjectWithoutVirtualDisplays_KeepsCurrentOnes()
    {
        var projectPath = Path.Combine(_dir, "old.livecue");
        File.WriteAllText(projectPath, """{ "Name": "Vecchio", "Cues": [] }""");
        var current = new List<VirtualDisplay> { new() { Name = "Esistente" } };

        ProjectFile.Load(new CueList(), current, projectPath);

        Assert.Equal("Esistente", Assert.Single(current).Name);
    }
}
