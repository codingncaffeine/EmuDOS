using System.Net.Http;
using System.Text.Json;
using EmuDOS.Core.Infrastructure;
using EmuDOS.Metadata;
using Xunit;
using Xunit.Abstractions;

namespace EmuDOS.Tests;

/// <summary>
/// Opt-in live probe of ScreenScraper title matching, using the saved login. Set
/// EMUDOS_SS_DIAG_GAMES to a semicolon-separated list of titles to check; for each it prints whether
/// SS matched and the metadata returned. Diagnostic only — it hits the live API and no-ops unless the
/// env var is set. Titles come from the environment so none are hard-coded into this public repo.
/// </summary>
public class ScreenScraperMatchDiagnostics
{
    private readonly ITestOutputHelper _out;
    public ScreenScraperMatchDiagnostics(ITestOutputHelper output) => _out = output;

    [Fact]
    public async Task Probe_titles_from_env()
    {
        var list = Environment.GetEnvironmentVariable("EMUDOS_SS_DIAG_GAMES");
        if (string.IsNullOrWhiteSpace(list))
            return;
        var games = list.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var settingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EmuDOS", "settings.json");
        var settings = JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(settingsPath))!;
        _out.WriteLine($"login configured: {!string.IsNullOrWhiteSpace(settings.ScreenScraperUser)}");

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
        var client = new ScreenScraperClient(http, settings.ScreenScraperUser, settings.ScreenScraperPassword);

        foreach (var g in games)
        {
            string result;
            try
            {
                var md = await client.FetchMetadataAsync(g);
                result = md is null
                    ? "NO MATCH"
                    : $"OK    name=\"{md.Name}\"  year={md.Year}  dev={md.Developer ?? md.Publisher}";
            }
            catch (Exception ex)
            {
                result = $"ERROR {ex.GetType().Name}: {ex.Message}";
            }
            _out.WriteLine($"{g,-42} => {result}");
        }
    }
}
