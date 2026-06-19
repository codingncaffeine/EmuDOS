using EmuDOS.Core.Catalog;

namespace EmuDOS.Tests.Integration;

/// <summary>
/// Opt-in: seeds the app's real catalog.db from an extracted eXoDOS "!dos" folder. Runs when
/// <c>EMUDOS_EXODOS_DOS</c> points at that folder; otherwise a no-op.
/// </summary>
public class ExoDosSeedLiveTests
{
    [Fact]
    public void Seeds_the_app_catalog_from_exodos()
    {
        var dosRoot = Environment.GetEnvironmentVariable("EMUDOS_EXODOS_DOS");
        if (string.IsNullOrWhiteSpace(dosRoot))
            return;

        var catalogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "EmuDOS", "Catalog", "catalog.db");

        var catalog = new CatalogDatabase(catalogPath);
        var count = new ExoDosCatalogSeeder().SeedFromExoDos(dosRoot, catalog);

        Assert.True(count > 1000, $"only {count} entries seeded from {dosRoot}");
        // Common titles resolve by name (the eXoDOS launch exe is hidden behind run.bat).
        Assert.NotNull(catalog.MatchByTitle("Doom"));
        Assert.NotNull(catalog.MatchByTitle("Lemmings"));
    }

    /// <summary>Diagnostic: how many of the user's game folders match by title. Writes a report.</summary>
    [Fact]
    public void Reports_title_match_rate_for_user_games()
    {
        var gamesDir = Environment.GetEnvironmentVariable("EMUDOS_USER_GAMES");
        if (string.IsNullOrWhiteSpace(gamesDir) || !Directory.Exists(gamesDir))
            return;

        var catalog = new CatalogDatabase(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "EmuDOS", "Catalog", "catalog.db"));

        var folders = Directory.GetDirectories(gamesDir).Select(Path.GetFileName).ToList();
        Assert.NotEmpty(folders);
        var unmatched = folders.Where(f => catalog.MatchByTitle(f!) is null).ToList();

        File.WriteAllText(Path.Combine(Path.GetTempPath(), "emudos-match-report.txt"),
            $"matched {folders.Count - unmatched.Count}/{folders.Count}\nUNMATCHED:\n{string.Join("\n", unmatched)}");
    }
}
