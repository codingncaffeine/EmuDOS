using System.Net.Http;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace EmuDOS.Metadata;

/// <summary>
/// Fetches DOS box art from the ScreenScraper.fr API v2. App-level dev credentials live in
/// the gitignored <see cref="Secrets"/>; the user's own ScreenScraper login is supplied per
/// instance (the API requires a registered account, but dev-cred-only access works at a low
/// quota).
/// </summary>
public sealed partial class ScreenScraperClient
{
    private const string BaseUrl = "https://www.screenscraper.fr/api2/";
    private const string SoftName = "EmuDOS";
    private const int DosSystemId = 135; // ScreenScraper "PC Dos"

    private static readonly string[] RegionPreference = ["us", "wor", "world", "eu", "jp"];
    private static readonly string[] BoxTypePreference = ["box-2D", "box-3D"];

    private readonly HttpClient _http;
    private readonly string _user;
    private readonly string _password;

    public ScreenScraperClient(HttpClient http, string user, string password)
    {
        _http = http;
        _user = user ?? string.Empty;
        _password = password ?? string.Empty;
    }

    /// <summary>
    /// Find the best box-art URL for a DOS game, trying the title and a cleaned-up variant and
    /// preferring 2D box art (falling back to 3D). Null if nothing matches.
    /// </summary>
    public async Task<string?> FindBoxArtUrlAsync(string gameName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameName);

        foreach (var candidate in NameCandidates(gameName))
        {
            var url = await FetchBoxForNameAsync(candidate, cancellationToken);
            if (url is not null)
                return url;
        }

        return null;
    }

    /// <summary>
    /// Verify the configured user login (with the dev creds) via <c>ssuserInfos.php</c>.
    /// Returns true only when ScreenScraper recognises the account.
    /// </summary>
    public async Task<bool> ValidateLoginAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _http.GetAsync($"{BaseUrl}ssuserInfos.php?{Auth()}", cancellationToken);
            if (!response.IsSuccessStatusCode)
                return false;

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var doc = JsonNode.Parse(body);
            return (doc?["response"]?["ssuser"] ?? doc?["ssuser"]) is not null;
        }
        catch
        {
            return false;
        }
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

    private async Task<string?> FetchBoxForNameAsync(string gameName, CancellationToken cancellationToken)
    {
        var url = $"{BaseUrl}jeuInfos.php?{Auth()}&systemeid={DosSystemId}&romnom={Esc(gameName)}";
        using var response = await _http.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return null;

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        JsonNode? doc;
        try { doc = JsonNode.Parse(body); }
        catch { return null; }

        var medias = doc?["response"]?["jeu"]?["medias"]?.AsArray();
        return medias is null ? null : PickBox(medias);
    }

    /// <summary>Original title, then a variant with collection/episode noise stripped.</summary>
    private static IEnumerable<string> NameCandidates(string name)
    {
        yield return name;
        var cleaned = CleanTitle(name);
        if (!string.IsNullOrWhiteSpace(cleaned)
            && !cleaned.Equals(name, StringComparison.OrdinalIgnoreCase))
        {
            yield return cleaned;
        }
    }

    private static string CleanTitle(string name)
    {
        // Drop "Episode 1", "Pack 2", "Disk 3", trailing collection words, and stray punctuation.
        var s = EpisodePackRegex().Replace(name, " ");
        s = CollectionWordRegex().Replace(s, " ");
        s = Regex.Replace(s, @"\s+", " ").Trim().Trim('-', ':', '–').Trim();
        return s;
    }

    private static string? PickBox(JsonArray medias)
    {
        foreach (var boxType in BoxTypePreference)
        {
            var boxes = medias
                .Where(m => (m?["type"]?.GetValue<string>() ?? string.Empty)
                    .StartsWith(boxType, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (boxes.Count == 0)
                continue;

            foreach (var region in RegionPreference)
            {
                var match = boxes.FirstOrDefault(b =>
                    string.Equals(b?["region"]?.GetValue<string>(), region, StringComparison.OrdinalIgnoreCase)
                    || (b?["type"]?.GetValue<string>() ?? string.Empty)
                        .EndsWith("-" + region, StringComparison.OrdinalIgnoreCase));
                if (match?["url"]?.GetValue<string>() is { Length: > 0 } regionalUrl)
                    return regionalUrl;
            }

            if (boxes[0]?["url"]?.GetValue<string>() is { Length: > 0 } anyUrl)
                return anyUrl;
        }

        return null;
    }

    private string Auth() =>
        $"devid={Esc(Secrets.ScreenScraperDevId)}&devpassword={Esc(Secrets.ScreenScraperDevPass)}"
        + $"&softname={Esc(SoftName)}&output=json"
        + $"&ssid={Esc(_user)}&sspassword={Esc(_password)}";

    private static string Esc(string value) => Uri.EscapeDataString(value);

    [GeneratedRegex(@"\b(episode|ep|pack|disk|disc|volume|vol)\s*\d+\b", RegexOptions.IgnoreCase)]
    private static partial Regex EpisodePackRegex();

    [GeneratedRegex(@"\b(trilogy|collection|anthology|compilation|edition|series)\b", RegexOptions.IgnoreCase)]
    private static partial Regex CollectionWordRegex();
}
