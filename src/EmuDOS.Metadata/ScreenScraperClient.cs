using System.Net.Http;
using System.Text.Json.Nodes;

namespace EmuDOS.Metadata;

/// <summary>
/// Fetches DOS box art from the ScreenScraper.fr API v2. App-level dev credentials live in
/// the gitignored <see cref="Secrets"/>; the user's own ScreenScraper login is supplied per
/// instance (the API requires a registered account).
/// </summary>
public sealed class ScreenScraperClient
{
    private const string BaseUrl = "https://www.screenscraper.fr/api2/";
    private const string SoftName = "EmuDOS";
    private const int DosSystemId = 135; // ScreenScraper "PC Dos"

    // Region preference for picking a single cover.
    private static readonly string[] RegionPreference = ["us", "wor", "world", "eu", "jp"];

    private readonly HttpClient _http;
    private readonly string _user;
    private readonly string _password;

    public ScreenScraperClient(HttpClient http, string user, string password)
    {
        _http = http;
        _user = user ?? string.Empty;
        _password = password ?? string.Empty;
    }

    /// <summary>Find the best 2D box-art URL for a DOS game by name, or null if none.</summary>
    public async Task<string?> FindBoxArtUrlAsync(string gameName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameName);

        var url = $"{BaseUrl}jeuInfos.php?{Auth()}&systemeid={DosSystemId}&romnom={Esc(gameName)}";
        using var response = await _http.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return null;

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        JsonNode? doc;
        try { doc = JsonNode.Parse(body); }
        catch { return null; } // ScreenScraper returns non-JSON on some errors

        var medias = doc?["response"]?["jeu"]?["medias"]?.AsArray();
        return medias is null ? null : PickBox2D(medias);
    }

    /// <summary>Download an image URL to bytes, or null on failure.</summary>
    public async Task<byte[]?> DownloadAsync(string url, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        using var response = await _http.GetAsync(url, cancellationToken);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadAsByteArrayAsync(cancellationToken)
            : null;
    }

    private string Auth() =>
        $"devid={Esc(Secrets.ScreenScraperDevId)}&devpassword={Esc(Secrets.ScreenScraperDevPass)}"
        + $"&softname={Esc(SoftName)}&output=json"
        + $"&ssid={Esc(_user)}&sspassword={Esc(_password)}";

    private static string? PickBox2D(JsonArray medias)
    {
        var boxes = medias
            .Where(m => (m?["type"]?.GetValue<string>() ?? string.Empty)
                .StartsWith("box-2D", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (boxes.Count == 0)
            return null;

        foreach (var region in RegionPreference)
        {
            var match = boxes.FirstOrDefault(b =>
                string.Equals(b?["region"]?.GetValue<string>(), region, StringComparison.OrdinalIgnoreCase)
                || (b?["type"]?.GetValue<string>() ?? string.Empty)
                    .EndsWith("-" + region, StringComparison.OrdinalIgnoreCase));
            if (match?["url"]?.GetValue<string>() is { Length: > 0 } url)
                return url;
        }

        return boxes[0]?["url"]?.GetValue<string>();
    }

    private static string Esc(string value) => Uri.EscapeDataString(value);
}
