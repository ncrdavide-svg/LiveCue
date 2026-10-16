namespace LiveCue.Core.Models;

/// <summary>
/// A cue that references a file on disk. Implemented by MediaCue (audio/video) and ImageCue, two
/// otherwise-unrelated branches of the Cue hierarchy, so ProjectFile can find and rewrite every
/// cue's file path relative to the project without needing a type check per cue type.
/// </summary>
public interface IFileCue
{
    string FilePath { get; set; }
}
