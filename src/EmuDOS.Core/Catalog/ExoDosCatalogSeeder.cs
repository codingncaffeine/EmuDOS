using System.Text.RegularExpressions;
using EmuDOS.Core.Model;

namespace EmuDOS.Core.Catalog;

/// <summary>
/// Builds the curated catalog from an eXoDOS Lite "!dos" metadata tree: each game is a folder
/// (a short code) containing a <c>dosbox.conf</c> and a <c>{Title} (YYYY).bat</c> launcher.
/// We keep only the SETTINGS from the conf (the eXoDOS [autoexec] mounts eXoDOS-specific paths,
/// so it's discarded), use the launcher's filename as the title, and use the executable named
/// in the autoexec as the telltale that matches a user's game by content.
/// </summary>
public sealed partial class ExoDosCatalogSeeder
{
    private static readonly string[] AutoexecControlPrefixes =
        ["mount", "imgmount", "cd ", "cd\\", "cls", "echo", "rem", "set ", "keyb", "choice", "pause", "exit", "del ", "copy", "if ", "call", "@call"];

    public int SeedFromExoDos(string dosRoot, CatalogDatabase catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var entries = BuildEntries(dosRoot).ToList();
        catalog.Build(entries);
        return entries.Count;
    }

    public IEnumerable<CatalogEntry> BuildEntries(string dosRoot)
    {
        if (!Directory.Exists(dosRoot))
            yield break;

        foreach (var gameDir in Directory.EnumerateDirectories(dosRoot))
        {
            var conf = Path.Combine(gameDir, "dosbox.conf");
            if (!File.Exists(conf))
                continue;

            var title = DeriveTitle(gameDir);
            var parsed = DosBoxConfParser.Parse(File.ReadAllText(conf), title);

            // The exe is a bonus telltale when the autoexec runs it directly; many eXoDOS games
            // launch via a run.bat we can't see, so they match by title instead.
            var telltale = ExtractLaunchExe(parsed.Launch.PreCommands);

            // Keep settings only; drop the eXoDOS-specific autoexec/launch.
            var profile = parsed with
            {
                Launch = new LaunchSpec(),
                Origin = ProfileOrigin.CuratedBase,
            };

            yield return new CatalogEntry
            {
                Id = new DirectoryInfo(gameDir).Name,
                Title = title,
                Telltales = telltale is null ? [] : [telltale],
                Profile = profile,
            };
        }
    }

    private static string DeriveTitle(string gameDir)
    {
        var bat = Directory.EnumerateFiles(gameDir, "*.bat")
            .Select(Path.GetFileName)
            .FirstOrDefault(n => n is not null && !n.Equals("install.bat", StringComparison.OrdinalIgnoreCase));

        var name = bat is null
            ? new DirectoryInfo(gameDir).Name
            : Path.GetFileNameWithoutExtension(bat);

        return YearSuffix().Replace(name, string.Empty).Trim();
    }

    /// <summary>The executable the autoexec launches (basename, lowercased), or null.</summary>
    private static string? ExtractLaunchExe(IReadOnlyList<string> autoexec)
    {
        foreach (var raw in autoexec)
        {
            var line = raw.TrimStart('@').Trim();
            if (line.Length == 0)
                continue;

            var lower = line.ToLowerInvariant();
            if (lower is "c:" or "d:" || AutoexecControlPrefixes.Any(p => lower.StartsWith(p, StringComparison.Ordinal)))
                continue;

            var token = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries)[0];
            var exe = Path.GetFileNameWithoutExtension(token).ToLowerInvariant();
            if (exe.Length > 0)
                return exe;
        }

        return null;
    }

    [GeneratedRegex(@"\s*\(\d{4}\)\s*$")]
    private static partial Regex YearSuffix();
}
