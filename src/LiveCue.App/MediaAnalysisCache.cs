using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace LiveCue.App;

/// <summary>
/// Disk cache for the In/Out tab's scrubber data (waveform peaks, video thumbnails) - both are
/// slow to generate (decoding the whole file), so re-opening the same project shouldn't redo it.
/// Keyed by file path + size + last-write time, so editing/replacing the source file invalidates
/// its own cache entry automatically without needing to hash the file's actual bytes.
/// </summary>
internal static class MediaAnalysisCache
{
    private static readonly string Root =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LiveCue", "cache");

    public static string GetCacheDir(string filePath)
    {
        var info = new FileInfo(filePath);
        var key = $"{filePath}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..16];
        var dir = Path.Combine(Root, hash);
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static string WaveformFile(string cacheDir) => Path.Combine(cacheDir, "waveform.bin");

    public static string ThumbnailFile(string cacheDir, int index) => Path.Combine(cacheDir, $"thumb_{index}.jpg");

    public static void SaveWaveform(string cacheDir, float[] peaks)
    {
        using var stream = File.Create(WaveformFile(cacheDir));
        using var writer = new BinaryWriter(stream);
        foreach (var peak in peaks) writer.Write(peak);
    }

    public static float[]? LoadWaveform(string cacheDir)
    {
        var path = WaveformFile(cacheDir);
        if (!File.Exists(path)) return null;
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length == 0 || bytes.Length % 4 != 0) return null;
        var peaks = new float[bytes.Length / 4];
        Buffer.BlockCopy(bytes, 0, peaks, 0, bytes.Length);
        return peaks;
    }

    /// <returns>Existing cached thumbnail paths in order, or null if fewer than <paramref name="expectedCount"/> are cached.</returns>
    public static IReadOnlyList<string>? LoadThumbnails(string cacheDir, int expectedCount)
    {
        var paths = new List<string>();
        for (var i = 0; i < expectedCount; i++)
        {
            var path = ThumbnailFile(cacheDir, i);
            if (!File.Exists(path)) return null;
            paths.Add(path);
        }
        return paths;
    }
}
