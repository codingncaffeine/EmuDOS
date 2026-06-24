using System;
using System.Collections.Generic;
using System.IO;

namespace EmuDOS.Effects.Librashader;

/// <summary>One selectable downloaded libretro <c>.slangp</c> preset in the shader picker.</summary>
public sealed class ShaderPresetItem
{
    public string Display { get; init; } = "";
    /// <summary>Group header in the picker (e.g. "crt", "handheld").</summary>
    public string Category { get; init; } = "";
    /// <summary>Absolute path to the .slangp.</summary>
    public string AbsolutePath { get; init; } = "";
    /// <summary>Path relative to the slang root, '/'-normalized — the persistence key.</summary>
    public string RelativePath { get; init; } = "";
}

/// <summary>
/// Enumerates the downloaded libretro slang shader pack into picker entries. The pack is huge
/// (~2500 .slangp) and most of it (bezel/presets/include/test/spec/reshade/hdr) is not "pick one
/// effect" material, so those top-level folders are filtered out. Results are cached and invalidated
/// when the pack is re-downloaded (keyed off the <c>.installed</c> marker's timestamp).
/// </summary>
public static class ShaderCatalog
{
    private static readonly HashSet<string> ExcludedCategories =
        new(StringComparer.OrdinalIgnoreCase)
        { "bezel", "presets", "include", "test", "spec", "reshade", "hdr", "nes_raw_palette" };

    private static List<ShaderPresetItem>? _cache;
    private static long _cacheStamp = -1;
    private static readonly object _gate = new();

    /// <summary>Returns the filtered, category-grouped downloaded presets, or empty if the pack isn't
    /// installed. Cached until the pack is re-downloaded. Call off the UI thread (walks the tree).</summary>
    public static IReadOnlyList<ShaderPresetItem> GetDownloaded(string slangRoot)
    {
        try
        {
            string marker = Path.Combine(slangRoot, ".installed");
            if (!File.Exists(marker)) return Array.Empty<ShaderPresetItem>();
            long stamp = File.GetLastWriteTimeUtc(marker).Ticks;

            lock (_gate)
                if (_cache != null && _cacheStamp == stamp) return _cache;

            var list = new List<ShaderPresetItem>();
            foreach (var file in Directory.EnumerateFiles(slangRoot, "*.slangp", SearchOption.AllDirectories))
            {
                string rel = Path.GetRelativePath(slangRoot, file).Replace('\\', '/');
                int slash = rel.IndexOf('/');
                string cat = slash > 0 ? rel[..slash] : "misc";
                if (ExcludedCategories.Contains(cat)) continue;

                list.Add(new ShaderPresetItem
                {
                    Display = Path.GetFileNameWithoutExtension(file),
                    Category = cat,
                    AbsolutePath = file,
                    RelativePath = rel,
                });
            }
            list.Sort((a, b) =>
            {
                int c = string.Compare(a.Category, b.Category, StringComparison.OrdinalIgnoreCase);
                return c != 0 ? c : string.Compare(a.Display, b.Display, StringComparison.OrdinalIgnoreCase);
            });

            lock (_gate) { _cache = list; _cacheStamp = stamp; }
            return list;
        }
        catch
        {
            return Array.Empty<ShaderPresetItem>();
        }
    }

    /// <summary>Resolves a persisted relative path (or bare filename) to an absolute .slangp, or null.</summary>
    public static string? Resolve(string slangRoot, string relativeOrName)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(relativeOrName)) return null;

            string direct = Path.GetFullPath(Path.Combine(slangRoot, relativeOrName));
            if (File.Exists(direct)) return direct;

            string name = Path.GetFileName(relativeOrName);
            foreach (var p in Directory.EnumerateFiles(slangRoot, name, SearchOption.AllDirectories))
                if (!p.Replace('\\', '/').Contains("/bezel/", StringComparison.OrdinalIgnoreCase))
                    return p;
            return null;
        }
        catch
        {
            return null;
        }
    }
}
