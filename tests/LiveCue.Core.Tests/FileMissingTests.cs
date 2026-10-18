using LiveCue.Core.Models;
using LiveCue.Core.Services;

namespace LiveCue.Core.Tests;

public sealed class FileMissingTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "LiveCueTests-" + Guid.NewGuid());

    public FileMissingTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void MediaCue_FlagsMissingFile()
    {
        var cue = new AudioCue { FilePath = Path.Combine(_dir, "does-not-exist.wav") };
        Assert.True(cue.FileMissing);
    }

    [Fact]
    public void MediaCue_DoesNotFlagAnExistingFile()
    {
        var path = Path.Combine(_dir, "song.wav");
        File.WriteAllText(path, "");

        var cue = new AudioCue { FilePath = path };

        Assert.False(cue.FileMissing);
    }

    [Fact]
    public void MediaCue_UpdatesWhenFilePathChanges()
    {
        var path = Path.Combine(_dir, "clip.mp4");
        File.WriteAllText(path, "");
        var cue = new VideoCue { FilePath = path };
        Assert.False(cue.FileMissing);

        cue.FilePath = Path.Combine(_dir, "moved.mp4");

        Assert.True(cue.FileMissing);
    }

    [Fact]
    public void ImageCue_FlagsMissingFile()
    {
        var cue = new ImageCue { FilePath = Path.Combine(_dir, "does-not-exist.png") };
        Assert.True(cue.FileMissing);
    }

    [Fact]
    public void ImageCue_DoesNotFlagAnExistingFile()
    {
        var path = Path.Combine(_dir, "logo.png");
        File.WriteAllText(path, "");

        var cue = new ImageCue { FilePath = path };

        Assert.False(cue.FileMissing);
    }

    [Fact]
    public void FadeCue_NeverFlagsAsMissing_ItHasNoFileOfItsOwn()
    {
        var cue = new FadeCue();
        Assert.False(cue.FileMissing);
    }

    [Fact]
    public void ProjectFile_Load_ResolvesPathBeforeFlaggingMissing()
    {
        // Regression: the stored path is relative to the project folder, not whatever the
        // process's current directory happens to be - FileMissing must reflect the file after
        // ProjectFile.Load resolves it, not the raw (relative) path mid-deserialization.
        var mediaDir = Path.Combine(_dir, "media");
        Directory.CreateDirectory(mediaDir);
        var mediaPath = Path.Combine(mediaDir, "song.wav");
        File.WriteAllText(mediaPath, "");

        var list = new CueList();
        list.Cues.Add(new AudioCue { FilePath = mediaPath });
        var projectPath = Path.Combine(_dir, "show.livecue");
        ProjectFile.Save(list, Array.Empty<VirtualDisplay>(), projectPath);

        var loaded = new CueList();
        ProjectFile.Load(loaded, new List<VirtualDisplay>(), projectPath);

        Assert.False(loaded.Cues[0].FileMissing);
    }
}
