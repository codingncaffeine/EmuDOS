namespace EmuDOS.Core.Catalog;

/// <summary>
/// Builds the curated catalog from an eXoDOS-style tree: a directory of per-game folders,
/// each holding a <c>dosbox.conf</c> alongside the game files. Each folder becomes a catalog
/// entry — its config parsed into a profile, its executables used as telltales.
/// </summary>
/// <remarks>
/// Telltale derivation (non-installer executables in the folder) is a deliberately simple
/// default that's easy to tune once we measure it against the real eXoDOS corpus.
/// </remarks>
public sealed class CatalogSeeder
{
    private static readonly string[] ExecutableExtensions = [".exe", ".com", ".bat"];
    private static readonly string[] InstallerStems = ["install", "setup", "inst", "instalar"];

    /// <summary>Parse every game folder under <paramref name="dosRoot"/> and (re)build the catalog.</summary>
    public int SeedFromExoDos(string dosRoot, CatalogDatabase catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var entries = BuildEntries(dosRoot).ToList();
        catalog.Build(entries);
        return entries.Count;
    }

    /// <summary>The catalog entries derivable from an eXoDOS tree (no DB writes).</summary>
    public IEnumerable<CatalogEntry> BuildEntries(string dosRoot)
    {
        if (!Directory.Exists(dosRoot))
            yield break;

        foreach (var gameDir in Directory.EnumerateDirectories(dosRoot))
        {
            var conf = FindConf(gameDir);
            if (conf is null)
                continue;

            var telltales = DeriveTelltales(gameDir);
            if (telltales.Count == 0)
                continue; // can't identify this game from content

            var title = new DirectoryInfo(gameDir).Name;
            yield return new CatalogEntry
            {
                Id = title,
                Title = title,
                Telltales = telltales,
                Profile = DosBoxConfParser.Parse(File.ReadAllText(conf), title),
            };
        }
    }

    private static string? FindConf(string gameDir)
    {
        var standard = Path.Combine(gameDir, "dosbox.conf");
        if (File.Exists(standard))
            return standard;
        return Directory.EnumerateFiles(gameDir, "*.conf").FirstOrDefault();
    }

    private static IReadOnlyList<string> DeriveTelltales(string gameDir)
    {
        var executables = Directory.EnumerateFiles(gameDir, "*", SearchOption.AllDirectories)
            .Where(f => ExecutableExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .Select(f => Path.GetFileName(f).ToLowerInvariant())
            .Distinct()
            .ToList();

        var games = executables
            .Where(e => !InstallerStems.Contains(Path.GetFileNameWithoutExtension(e)))
            .ToList();

        return games.Count > 0 ? games : executables;
    }
}
