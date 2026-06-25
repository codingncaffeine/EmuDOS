using System.Text.Json;

namespace EmuDOS.Core.Library;

/// <summary>
/// Tracks which files in a folder game's <c>content/</c> are the originally-imported game vs. files
/// the game wrote at runtime (its in-game saves). dosbox_pure writes a folder game's saves in place
/// into content/, so we snapshot the content file list at import (a baseline of relpath → size +
/// modified-time) and treat anything new or changed since as a save. EmuDOS-generated and temp files
/// are excluded. (Iso games persist to a <c>.pure.zip</c> instead and don't use this.)
/// </summary>
public static class ContentBaseline
{
    private const string FileName = ".content-baseline.json";
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    private sealed record Entry(long Size, long Mtime);

    private static readonly string[] ExcludedNames = ["dosbox.bat", "emudos.m3u8"];

    private static bool IsExcluded(string rel)
    {
        var name = Path.GetFileName(rel).ToLowerInvariant();
        if (ExcludedNames.Contains(name))
            return true;
        if (name.StartsWith("gamecd."))    // staged bundled-CD image
            return true;
        var ext = Path.GetExtension(name);
        return ext is ".tmp" or ".swp"; // scratch / DOS swap files — not saves, and regenerate constantly
    }

    public static bool Exists(string savesDir) => File.Exists(Path.Combine(savesDir, FileName));

    /// <summary>Snapshot the current content as the baseline (overwrites any existing one).</summary>
    public static void Capture(string contentDir, string savesDir)
    {
        Directory.CreateDirectory(savesDir);
        File.WriteAllText(Path.Combine(savesDir, FileName), JsonSerializer.Serialize(BuildMap(contentDir), Json));
    }

    /// <summary>Capture a baseline only if none exists yet (so existing games start tracking saves).</summary>
    public static void CaptureIfMissing(string contentDir, string savesDir)
    {
        if (!Exists(savesDir))
            Capture(contentDir, savesDir);
    }

    /// <summary>Content files (relative paths) that are new or changed since the baseline — i.e. the
    /// game's in-game saves. Empty if there's no baseline yet.</summary>
    public static List<string> DiffSaves(string contentDir, string savesDir)
    {
        var result = new List<string>();
        Dictionary<string, Entry>? baseline = Load(savesDir);
        if (baseline is null || !Directory.Exists(contentDir))
            return result;

        foreach (var f in Directory.EnumerateFiles(contentDir, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(contentDir, f).Replace('\\', '/');
            if (IsExcluded(rel))
                continue;
            var fi = new FileInfo(f);
            if (!baseline.TryGetValue(rel, out var b) || b.Size != fi.Length || b.Mtime < fi.LastWriteTimeUtc.Ticks)
                result.Add(rel);
        }
        result.Sort(StringComparer.OrdinalIgnoreCase);
        return result;
    }

    private static Dictionary<string, Entry>? Load(string savesDir)
    {
        var path = Path.Combine(savesDir, FileName);
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<Dictionary<string, Entry>>(File.ReadAllText(path), Json)
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static Dictionary<string, Entry> BuildMap(string contentDir)
    {
        var map = new Dictionary<string, Entry>(StringComparer.Ordinal);
        if (!Directory.Exists(contentDir))
            return map;
        foreach (var f in Directory.EnumerateFiles(contentDir, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(contentDir, f).Replace('\\', '/');
            if (IsExcluded(rel))
                continue;
            var fi = new FileInfo(f);
            map[rel] = new Entry(fi.Length, fi.LastWriteTimeUtc.Ticks);
        }
        return map;
    }
}
