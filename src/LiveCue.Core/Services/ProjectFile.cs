using System.Text.Json;
using LiveCue.Core.Models;

namespace LiveCue.Core.Services;

/// <summary>
/// Saves/loads a show (cue list + virtual displays) to/from a JSON project file.
/// Media paths are stored relative to the project file, so a show folder (project + media)
/// can be moved or copied to another machine without breaking its cues.
/// </summary>
public static class ProjectFile
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public static void Save(CueList cueList, IEnumerable<VirtualDisplay> virtualDisplays, string path)
    {
        var projectDir = GetProjectDirectory(path);

        // Serialize the live objects as-is, then rewrite paths in the JSON only — the cues in
        // memory keep their absolute paths, which is what playback needs.
        var dto = new ProjectDto(cueList.Name, cueList.Cues.ToList(), virtualDisplays.ToList());
        var root = JsonSerializer.SerializeToNode(dto, Options)!;
        foreach (var cueNode in root[nameof(ProjectDto.Cues)]!.AsArray())
        {
            if (cueNode?[nameof(MediaCue.FilePath)]?.GetValue<string>() is { Length: > 0 } filePath)
                cueNode[nameof(MediaCue.FilePath)] = ToStoredPath(filePath, projectDir);
        }

        File.WriteAllText(path, root.ToJsonString(Options));
    }

    /// <summary>
    /// Replaces the cue list (and, if the file has any, the virtual displays) with the file's contents.
    /// Older project files have no virtual displays; the current ones are then left untouched.
    /// </summary>
    public static void Load(CueList cueList, ICollection<VirtualDisplay> virtualDisplays, string path)
    {
        var json = File.ReadAllText(path);
        var dto = JsonSerializer.Deserialize<ProjectDto>(json, Options);
        if (dto is null) return;

        var projectDir = GetProjectDirectory(path);
        foreach (var cue in dto.Cues.OfType<MediaCue>())
            cue.FilePath = ResolveStoredPath(cue.FilePath, projectDir);

        if (dto.VirtualDisplays is { Count: > 0 })
        {
            virtualDisplays.Clear();
            foreach (var virtualDisplay in dto.VirtualDisplays)
                virtualDisplays.Add(virtualDisplay);
        }

        cueList.Name = dto.Name;
        cueList.Cues.Clear();
        foreach (var cue in dto.Cues)
            cueList.Cues.Add(cue);
        cueList.SelectedIndex = cueList.Cues.Count > 0 ? 0 : -1;
    }

    private static string GetProjectDirectory(string projectPath) =>
        Path.GetDirectoryName(Path.GetFullPath(projectPath))!;

    /// <summary>Relative to the project folder when possible; absolute when on another drive (no relative path exists).</summary>
    internal static string ToStoredPath(string filePath, string projectDir)
    {
        if (!Path.IsPathRooted(filePath)) return filePath;
        var relative = Path.GetRelativePath(projectDir, filePath);
        return Path.IsPathRooted(relative) ? filePath : relative;
    }

    internal static string ResolveStoredPath(string storedPath, string projectDir)
    {
        if (string.IsNullOrEmpty(storedPath)) return storedPath;
        if (!Path.IsPathRooted(storedPath))
            return Path.GetFullPath(Path.Combine(projectDir, storedPath));

        // Absolute path (older project files, or media on another drive): if it no longer exists,
        // look for a file with the same name next to the project — the usual "show folder" layout.
        if (File.Exists(storedPath)) return storedPath;
        var besideProject = Path.Combine(projectDir, Path.GetFileName(storedPath));
        return File.Exists(besideProject) ? besideProject : storedPath;
    }

    private record ProjectDto(string Name, List<Cue> Cues, List<VirtualDisplay>? VirtualDisplays);
}
