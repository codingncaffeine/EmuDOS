using System.IO.Compression;
using System.Text.Json;

namespace EmuDOS.Core.Library;

/// <summary>One save state on disk: the gzip'd machine snapshot, its thumbnail, and metadata.</summary>
public sealed record SaveStateInfo(string Id, string StatePath, string? ThumbPath, DateTime WhenUtc, string? Label);

/// <summary>
/// Manages a game's save states in its <c>saves/</c> folder. Each F5 writes a NEW state
/// (<c>state_{timestamp}.sav.gz</c>, never overwriting) with a thumbnail (<c>.png</c>) and a tiny
/// sidecar (<c>.json</c>); F8 loads the newest. States are gzip-compressed at rest because they
/// accumulate. The Manage window lists/loads/deletes them.
/// </summary>
public static class SaveStateStore
{
    private const string StateExt = ".sav.gz";
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    private sealed record Sidecar(DateTime WhenUtc, string? Label);

    /// <summary>Write a new save state (+ optional thumbnail PNG). Returns its descriptor.</summary>
    public static SaveStateInfo Write(string savesDir, byte[] state, byte[]? thumbnailPng, string? label = null)
    {
        Directory.CreateDirectory(savesDir);
        var now = DateTime.Now;
        var id = "state_" + now.ToString("yyyyMMdd-HHmmss");
        var basePath = Path.Combine(savesDir, id);
        for (int n = 1; File.Exists(basePath + StateExt); n++) // avoid same-second collisions
        {
            id = $"state_{now:yyyyMMdd-HHmmss}-{n}";
            basePath = Path.Combine(savesDir, id);
        }

        using (var fs = File.Create(basePath + StateExt))
        using (var gz = new GZipStream(fs, CompressionLevel.Optimal))
            gz.Write(state, 0, state.Length);

        string? thumbPath = null;
        if (thumbnailPng is { Length: > 0 })
        {
            thumbPath = basePath + ".png";
            File.WriteAllBytes(thumbPath, thumbnailPng);
        }

        var whenUtc = now.ToUniversalTime();
        File.WriteAllText(basePath + ".json", JsonSerializer.Serialize(new Sidecar(whenUtc, label), Json));
        return new SaveStateInfo(id, basePath + StateExt, thumbPath, whenUtc, label);
    }

    /// <summary>All save states for a game, newest first.</summary>
    public static List<SaveStateInfo> List(string savesDir)
    {
        var list = new List<SaveStateInfo>();
        if (!Directory.Exists(savesDir))
            return list;
        foreach (var sav in Directory.EnumerateFiles(savesDir, "state_*" + StateExt))
        {
            var basePath = sav[..^StateExt.Length];
            var id = Path.GetFileName(basePath);
            var thumb = basePath + ".png";
            var when = File.GetLastWriteTimeUtc(sav);
            string? label = null;
            try
            {
                var json = basePath + ".json";
                if (File.Exists(json) &&
                    JsonSerializer.Deserialize<Sidecar>(File.ReadAllText(json), Json) is { } sc)
                {
                    when = sc.WhenUtc;
                    label = sc.Label;
                }
            }
            catch { /* fall back to file time */ }
            list.Add(new SaveStateInfo(id, sav, File.Exists(thumb) ? thumb : null, when, label));
        }
        list.Sort((a, b) => b.WhenUtc.CompareTo(a.WhenUtc));
        return list;
    }

    /// <summary>The most recent save state, or null if there are none.</summary>
    public static SaveStateInfo? Newest(string savesDir) => List(savesDir).FirstOrDefault();

    /// <summary>Decompress a save state's machine snapshot, or null if it can't be read.</summary>
    public static byte[]? ReadState(SaveStateInfo info)
    {
        try
        {
            using var fs = File.OpenRead(info.StatePath);
            using var gz = new GZipStream(fs, CompressionMode.Decompress);
            using var ms = new MemoryStream();
            gz.CopyTo(ms);
            return ms.ToArray();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Delete a save state and its thumbnail/sidecar.</summary>
    public static void Delete(SaveStateInfo info)
    {
        var basePath = info.StatePath[..^StateExt.Length];
        TryDelete(info.StatePath);
        TryDelete(basePath + ".png");
        TryDelete(basePath + ".json");
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* best effort */ }
    }
}
