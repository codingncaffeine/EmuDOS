using EmuDOS.Core.Infrastructure;

namespace EmuDOS.Core.Library;

/// <summary>
/// Box art kept by game title outside any gamebox, so deleting a game from the library doesn't
/// lose its cover — re-importing the same game restores it instantly with no re-download.
/// Populated whenever art is obtained (fetched or restored) and on delete as a safety net.
/// </summary>
public sealed class ArtCache
{
    private const string BoxFrontFileName = "box-front.png";

    private readonly string _dir;

    public ArtCache(AppPaths paths)
    {
        _dir = Path.Combine(paths.DataRoot, "ArtCache");
        Directory.CreateDirectory(_dir);
    }

    public bool Has(string title) => File.Exists(PathFor(title));

    /// <summary>Copy a gamebox's box front into the cache (no-op if it doesn't exist).</summary>
    public void Stash(string title, string boxFrontFile)
    {
        if (string.IsNullOrWhiteSpace(title) || !File.Exists(boxFrontFile))
            return;
        try { File.Copy(boxFrontFile, PathFor(title), overwrite: true); }
        catch { /* caching is best-effort */ }
    }

    /// <summary>Restore cached art into a gamebox media dir. Returns true if art was placed.</summary>
    public bool TryRestore(string title, string mediaDir)
    {
        var cached = PathFor(title);
        if (!File.Exists(cached))
            return false;
        try
        {
            Directory.CreateDirectory(mediaDir);
            File.Copy(cached, Path.Combine(mediaDir, BoxFrontFileName), overwrite: true);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Copy a gamebox's metadata.json into the cache (no-op if absent), so descriptive
    /// metadata survives deleting the game and is reused on re-import.</summary>
    public void StashMetadata(string title, string metadataFile)
    {
        if (string.IsNullOrWhiteSpace(title) || !File.Exists(metadataFile))
            return;
        try { File.Copy(metadataFile, MetaPathFor(title), overwrite: true); }
        catch { /* caching is best-effort */ }
    }

    /// <summary>Restore cached metadata.json into a gamebox. Returns true if it was placed.</summary>
    public bool TryRestoreMetadata(string title, string gameboxRoot)
    {
        var cached = MetaPathFor(title);
        if (!File.Exists(cached))
            return false;
        try
        {
            File.Copy(cached, new Gamebox(gameboxRoot).MetadataPath, overwrite: true);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private string MetaPathFor(string title) => Path.Combine(_dir, Key(title) + ".meta.json");

    private string PathFor(string title) => Path.Combine(_dir, Key(title) + ".png");

    private static string Key(string title)
    {
        var s = title.Trim().ToLowerInvariant();
        foreach (var c in Path.GetInvalidFileNameChars())
            s = s.Replace(c, '_');
        return s.Length == 0 ? "_" : s;
    }
}
