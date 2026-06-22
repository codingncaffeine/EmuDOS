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
            var url = await FetchMediaForNameAsync(candidate, PickBox, cancellationToken);
            if (url is not null)
                return url;
        }

        return null;
    }

    /// <summary>
    /// Find the best 3D box-render URL for a DOS game (ScreenScraper's <c>box-3D</c> media).
    /// Null if the game has no 3D box. There is no SteamGridDB equivalent, so this is SS-only.
    /// </summary>
    public async Task<string?> FindBox3DUrlAsync(string gameName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameName);

        foreach (var candidate in NameCandidates(gameName))
        {
            var url = await FetchMediaForNameAsync(candidate, PickBox3D, cancellationToken);
            if (url is not null)
                return url;
        }

        return null;
    }

    /// <summary>Find the game's manual (PDF) URL, or null if ScreenScraper has none.</summary>
    public async Task<string?> FindManualUrlAsync(string gameName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameName);

        foreach (var candidate in NameCandidates(gameName))
        {
            var url = await FetchMediaForNameAsync(candidate, PickManual, cancellationToken);
            if (url is not null)
                return url;
        }

        return null;
    }

    /// <summary>Find a gameplay video-snap URL (prefers the smaller "video-normalized" media, falling
    /// back to "video"), or null if ScreenScraper has none.</summary>
    public async Task<string?> FindVideoUrlAsync(string gameName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameName);

        foreach (var candidate in NameCandidates(gameName))
        {
            var url = await FetchMediaForNameAsync(candidate, PickVideo, cancellationToken);
            if (url is not null)
                return url;
        }

        return null;
    }

    /// <summary>
    /// Fetch descriptive metadata (year, developer, publisher, genre, synopsis) for a DOS game,
    /// reusing the same <c>jeuInfos.php</c> endpoint the art path calls. Null if nothing matched.
    /// </summary>
    public async Task<EmuDOS.Core.Model.GameMetadata?> FetchMetadataAsync(
        string gameName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameName);

        foreach (var candidate in NameCandidates(gameName))
        {
            var url = $"{BaseUrl}jeuInfos.php?{Auth()}&systemeid={DosSystemId}&romnom={Esc(candidate)}";
            using var response = await _http.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
                continue;
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (ExtractMetadata(body) is { } md)
                return md;
        }

        return null;
    }

    private static EmuDOS.Core.Model.GameMetadata? ExtractMetadata(string json)
    {
        JsonNode? doc;
        try { doc = JsonNode.Parse(json); }
        catch { return null; }

        var jeu = doc?["response"]?["jeu"];
        if (jeu is null)
            return null;

        var year = PickRegional(jeu["dates"]?.AsArray(), "text", ["us", "wor", "ss", "eu", "jp"]);
        if (!string.IsNullOrEmpty(year) && year.Length >= 4 && int.TryParse(year[..4], out _))
            year = year[..4];

        var developer = jeu["developpeur"]?["text"]?.GetValue<string>();
        var publisher = jeu["editeur"]?["text"]?.GetValue<string>();

        string? genre = null;
        var genres = jeu["genres"]?.AsArray();
        if (genres is { Count: > 0 })
            genre = PickRegional(genres[0]?["noms"]?.AsArray(), "text", ["en", "us", "wor"], langField: "langue");

        var description = PickRegional(jeu["synopsis"]?.AsArray(), "text", ["en", "us", "wor"], langField: "langue");

        var md = new EmuDOS.Core.Model.GameMetadata
        {
            Year = Nz(year),
            Developer = Nz(developer),
            Publisher = Nz(publisher),
            Genre = Nz(genre),
            Description = Nz(description),
        };
        return md.IsEmpty ? null : md;
    }

    // Walk a ScreenScraper regional array, preferring entries whose region/langue matches one of
    // the preferred values; fall back to the first non-empty text.
    private static string? PickRegional(JsonArray? arr, string textField, string[] preferred, string langField = "region")
    {
        if (arr is null || arr.Count == 0)
            return null;
        foreach (var pref in preferred)
            foreach (var entry in arr)
                if (string.Equals(entry?[langField]?.GetValue<string>(), pref, StringComparison.OrdinalIgnoreCase)
                    && entry?[textField]?.GetValue<string>() is { Length: > 0 } text)
                    return text;
        foreach (var entry in arr)
            if (entry?[textField]?.GetValue<string>() is { Length: > 0 } text)
                return text;
        return null;
    }

    private static string? Nz(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    /// <summary>
    /// Verify the configured user login (with the dev creds) via <c>ssuserInfos.php</c>.
    /// Returns whether the account is recognised and its <c>maxthreads</c> allowance — the number
    /// of concurrent API requests the account may make (paid tiers get more; free/anonymous = 1).
    /// </summary>
    public async Task<(bool Ok, int MaxThreads)> ValidateLoginAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _http.GetAsync($"{BaseUrl}ssuserInfos.php?{Auth()}", cancellationToken);
            if (!response.IsSuccessStatusCode)
                return (false, 1);

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var doc = JsonNode.Parse(body);
            var ssuser = doc?["response"]?["ssuser"] ?? doc?["ssuser"];
            if (ssuser is null)
                return (false, 1);

            // ScreenScraper returns maxthreads as a string; be tolerant of a numeric node too.
            var maxThreads = 1;
            if (ssuser["maxthreads"] is { } node && int.TryParse(node.ToString(), out var parsed) && parsed > 0)
                maxThreads = parsed;

            return (true, maxThreads);
        }
        catch
        {
            return (false, 1);
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

    private async Task<string?> FetchMediaForNameAsync(
        string gameName, Func<JsonArray, string?> pick, CancellationToken cancellationToken)
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
        return medias is null ? null : pick(medias);
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

    private static string? PickBox3D(JsonArray medias) => PickBoxOfType(medias, "box-3D");

    // Pick the best media whose type starts with boxType, honouring region preference.
    private static string? PickBoxOfType(JsonArray medias, string boxType)
    {
        var boxes = medias
            .Where(m => (m?["type"]?.GetValue<string>() ?? string.Empty)
                .StartsWith(boxType, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (boxes.Count == 0)
            return null;

        foreach (var region in RegionPreference)
        {
            var match = boxes.FirstOrDefault(b =>
                string.Equals(b?["region"]?.GetValue<string>(), region, StringComparison.OrdinalIgnoreCase)
                || (b?["type"]?.GetValue<string>() ?? string.Empty)
                    .EndsWith("-" + region, StringComparison.OrdinalIgnoreCase));
            if (match?["url"]?.GetValue<string>() is { Length: > 0 } regionalUrl)
                return regionalUrl;
        }

        return boxes[0]?["url"]?.GetValue<string>() is { Length: > 0 } anyUrl ? anyUrl : null;
    }

    // Prefer "video-normalized" (smaller, consistent), fall back to "video". Snaps carry no region.
    private static string? PickVideo(JsonArray medias)
    {
        foreach (var wanted in new[] { "video-normalized", "video" })
        {
            var match = medias.FirstOrDefault(m =>
                string.Equals(m?["type"]?.GetValue<string>(), wanted, StringComparison.OrdinalIgnoreCase));
            if (match?["url"]?.GetValue<string>() is { Length: > 0 } url)
                return url;
        }
        return null;
    }

    private static string? PickManual(JsonArray medias)
    {
        var manuals = medias
            .Where(m => string.Equals(m?["type"]?.GetValue<string>(), "manuel", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (manuals.Count == 0)
            return null;

        foreach (var region in RegionPreference)
        {
            var match = manuals.FirstOrDefault(m =>
                string.Equals(m?["region"]?.GetValue<string>(), region, StringComparison.OrdinalIgnoreCase));
            if (match?["url"]?.GetValue<string>() is { Length: > 0 } regionalUrl)
                return regionalUrl;
        }

        return manuals[0]?["url"]?.GetValue<string>();
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
