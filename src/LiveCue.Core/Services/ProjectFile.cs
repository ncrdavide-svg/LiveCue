using System.Text.Json;
using LiveCue.Core.Models;

namespace LiveCue.Core.Services;

/// <summary>Saves/loads a CueList to/from a JSON project file.</summary>
public static class ProjectFile
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public static void Save(CueList cueList, string path)
    {
        var dto = new ProjectDto(cueList.Name, cueList.Cues.ToList());
        File.WriteAllText(path, JsonSerializer.Serialize(dto, Options));
    }

    public static void Load(CueList cueList, string path)
    {
        var json = File.ReadAllText(path);
        var dto = JsonSerializer.Deserialize<ProjectDto>(json, Options);
        if (dto is null) return;

        cueList.Name = dto.Name;
        cueList.Cues.Clear();
        foreach (var cue in dto.Cues)
            cueList.Cues.Add(cue);
        cueList.SelectedIndex = cueList.Cues.Count > 0 ? 0 : -1;
    }

    private record ProjectDto(string Name, List<Cue> Cues);
}
