using System.Net.Http;
using EmuDOS.Metadata;

namespace EmuDOS.Tests.Integration;

/// <summary>
/// Opt-in: hits the live ScreenScraper API to fetch + download a DOS box cover. Uses the env
/// vars EMUDOS_SS_USER / EMUDOS_SS_PASS if set (otherwise anonymous dev-cred access). Runs
/// only when EMUDOS_LIVE_DOWNLOAD=1.
/// </summary>
public class ScreenScraperLiveTests
{
    [Fact]
    public async Task Fetches_and_downloads_a_dos_box_cover()
    {
        if (Environment.GetEnvironmentVariable("EMUDOS_LIVE_DOWNLOAD") != "1")
            return;

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };
        http.DefaultRequestHeaders.Add("User-Agent", "EmuDOS/1.0");
        var client = new ScreenScraperClient(
            http,
            Environment.GetEnvironmentVariable("EMUDOS_SS_USER") ?? string.Empty,
            Environment.GetEnvironmentVariable("EMUDOS_SS_PASS") ?? string.Empty);

        var url = await client.FindBoxArtUrlAsync("Doom");
        Assert.False(string.IsNullOrEmpty(url), "ScreenScraper returned no box-2D URL (quota/login?).");

        var bytes = await client.DownloadAsync(url!);
        Assert.NotNull(bytes);
        Assert.True(bytes!.Length > 1000, $"image too small: {bytes.Length} bytes");
        File.WriteAllBytes(
            @"C:\Users\gamer\source\repos\EmuDOS\local-notes\test-boxart.png", bytes);
    }
}
