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

        var medias = (await ResolveJeuAsync(gameName, cancellationToken))?["medias"]?.AsArray();
        return medias is null ? null : PickBox(medias);
    }

    /// <summary>
    /// Find the best 3D box-render URL for a DOS game (ScreenScraper's <c>box-3D</c> media).
    /// Null if the game has no 3D box. There is no SteamGridDB equivalent, so this is SS-only.
    /// </summary>
    public async Task<string?> FindBox3DUrlAsync(string gameName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameName);

        var medias = (await ResolveJeuAsync(gameName, cancellationToken))?["medias"]?.AsArray();
        return medias is null ? null : PickBox3D(medias);
    }

    /// <summary>Find the game's manual (PDF) URL, or null if ScreenScraper has none.</summary>
    public async Task<string?> FindManualUrlAsync(string gameName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameName);

        var medias = (await ResolveJeuAsync(gameName, cancellationToken))?["medias"]?.AsArray();
        return medias is null ? null : PickManual(medias);
    }

    // Extra media types offered in the Manage window (beyond box art / manual / video).
    private static readonly string[] ExtraTypes = ["wheel", "marquee", "fanart", "ss", "maps"];

    /// <summary>Find the available "extra" media (clear logo, marquee, fanart, screenshot, maps) for a
    /// game — one best URL per type, with its image format. Empty if none matched.</summary>
    public async Task<IReadOnlyList<(string Type, string Url, string Format)>> FindExtrasAsync(
        string gameName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameName);

        var medias = (await ResolveJeuAsync(gameName, cancellationToken))?["medias"]?.AsArray();
        if (medias is null)
            return [];

        var result = new List<(string, string, string)>();
        foreach (var type in ExtraTypes)
            if (PickByType(medias, type) is var (url, format) && url is { Length: > 0 })
                result.Add((type, url, string.IsNullOrEmpty(format) ? "png" : format));
        return result;
    }

    // Best (regional) URL + format for a media type.
    private static (string? Url, string? Format) PickByType(JsonArray medias, string type)
    {
        var items = medias.Where(m =>
            string.Equals(m?["type"]?.GetValue<string>(), type, StringComparison.OrdinalIgnoreCase)).ToList();
        if (items.Count == 0)
            return (null, null);

        JsonNode? chosen = null;
        foreach (var region in RegionPreference)
        {
            chosen = items.FirstOrDefault(m =>
                string.Equals(m?["region"]?.GetValue<string>(), region, StringComparison.OrdinalIgnoreCase));
            if (chosen is not null)
                break;
        }
        chosen ??= items[0];
        return (chosen?["url"]?.GetValue<string>(), chosen?["format"]?.GetValue<string>());
    }

    /// <summary>Find a gameplay video-snap URL (prefers the smaller "video-normalized" media, falling
    /// back to "video"), or null if ScreenScraper has none.</summary>
    public async Task<string?> FindVideoUrlAsync(string gameName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameName);

        var medias = (await ResolveJeuAsync(gameName, cancellationToken))?["medias"]?.AsArray();
        return medias is null ? null : PickVideo(medias);
    }

    /// <summary>
    /// Fetch descriptive metadata (year, developer, publisher, genre, synopsis) for a DOS game,
    /// reusing the same <c>jeuInfos.php</c> endpoint the art path calls. Null if nothing matched.
    /// </summary>
    public async Task<EmuDOS.Core.Model.GameMetadata?> FetchMetadataAsync(
        string gameName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameName);
        var jeu = await ResolveJeuAsync(gameName, cancellationToken);
        if (jeu is null)
            return null;
        var md = ExtractMetadataFromJeu(jeu);
        // Only expose the canonical Name for an auto-rename when we're confident it's the SAME game
        // (close name + compatible sequel number), so a fuzzy/wrong-sequel match can't rename a title.
        if (md is not null && md.Name is not null && !IsConfidentRename(gameName, md.Name))
            md = md with { Name = null };
        return md;
    }

    /// <summary>Resolve a lookup term to ScreenScraper's canonical game name (ungated — used by the
    /// manual "Rename from ScreenScraper" action where the user supplies the term). Null if no match.</summary>
    public async Task<string?> ResolveNameAsync(string gameName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameName);
        var jeu = await ResolveJeuAsync(gameName, cancellationToken);
        return jeu is null ? null : Nz(PickRegional(jeu["noms"]?.AsArray(), "text", ["us", "wor", "world", "eu", "ss"]));
    }

    private static EmuDOS.Core.Model.GameMetadata? ExtractMetadataFromJeu(JsonNode jeu)
    {
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

        var name = PickRegional(jeu["noms"]?.AsArray(), "text", ["us", "wor", "world", "eu", "ss"]);

        var md = new EmuDOS.Core.Model.GameMetadata
        {
            Name = Nz(name),
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

    // Resolve a game to its ScreenScraper "jeu" node, shared by every art/metadata/snap/manual lookup.
    // First the romnom candidates (DOS system, then all systems for multi-platform hits); if those all
    // miss, fall back to a fuzzy name search (jeuRecherche), which bridges titles SS lists under a
    // subtitle or different punctuation (e.g. "Police Quest 1" -> "Police Quest: In Pursuit of the
    // Death Angel", "Kings Quest 5" -> "King's Quest V", "7th Guest" -> "The 7th Guest").
    private async Task<JsonNode?> ResolveJeuAsync(string gameName, CancellationToken ct)
    {
        foreach (var candidate in NameCandidates(gameName))
            foreach (var systemPart in new[] { $"&systemeid={DosSystemId}", "" })
                if (await JeuInfosAsync($"{systemPart}&romnom={Esc(candidate)}", ct) is { } jeu)
                    return jeu;

        var gameId = await SearchBestGameIdAsync(gameName, ct);
        if (gameId is not null && await JeuInfosAsync($"&gameid={Esc(gameId)}", ct) is { } byId)
            return byId;

        return null;
    }

    private async Task<JsonNode?> JeuInfosAsync(string query, CancellationToken ct)
    {
        var url = $"{BaseUrl}jeuInfos.php?{Auth()}{query}";
        using var response = await _http.GetAsync(url, ct);
        if (!response.IsSuccessStatusCode)
            return null;
        var body = await response.Content.ReadAsStringAsync(ct);
        try { return JsonNode.Parse(body)?["response"]?["jeu"]; }
        catch { return null; }
    }

    // ScreenScraper's fuzzy name search. Scores every result by name similarity to the wanted title
    // (numeral-normalized, prefix + edit-distance) and returns the best id — so we don't grab a more
    // famous same-prefix game ("Duke Nukem 1" must not resolve to "Duke Nukem 3D"). Biased to DOS,
    // then all systems. Null if nothing scores well enough.
    private async Task<string?> SearchBestGameIdAsync(string gameName, CancellationToken ct)
    {
        var wanted = NormalizeForCompare(gameName);
        if (wanted.Length == 0)
            return null;

        // SS's search can return nothing for a trailing Arabic number against a Roman-numeral catalog
        // name ("Kings Quest 6" -> empty, but "Kings Quest" returns the series). So search with the
        // number dropped and numeral-swapped too, then score every result by the FULL wanted name so
        // the right entry still wins.
        var bare = StripNoise(gameName);
        var terms = new List<string>();
        foreach (var t in new[]
                 {
                     bare,
                     EpisodeToNumber(bare),                              // "...Episode 3" -> "...3"
                     DropTrailingNumber(bare),                          // series-wide search, score picks the entry
                     SwapNumeralStyle(bare, toRoman: true),             // "...6" -> "...VI"
                     SwapNumeralStyle(Possessive(bare), toRoman: true), // "Kings Quest 6" -> "King's Quest VI"
                 })
            if (!string.IsNullOrWhiteSpace(t) && !terms.Contains(t, StringComparer.OrdinalIgnoreCase))
                terms.Add(t);

        string? bestId = null;
        double bestScore = 0;
        foreach (var systemPart in new[] { $"&systemeid={DosSystemId}", "" })
        {
            foreach (var term in terms)
            {
                var url = $"{BaseUrl}jeuRecherche.php?{Auth()}{systemPart}&recherche={Esc(term)}";
                using var response = await _http.GetAsync(url, ct);
                if (!response.IsSuccessStatusCode)
                    continue;
                var body = await response.Content.ReadAsStringAsync(ct);
                try
                {
                    var jeux = JsonNode.Parse(body)?["response"]?["jeux"]?.AsArray();
                    if (jeux is null)
                        continue;
                    foreach (var jeu in jeux)
                    {
                        if (jeu?["id"]?.ToString() is not { Length: > 0 } id)
                            continue;
                        foreach (var nom in GameNames(jeu))
                        {
                            var score = BestScore(wanted, nom);
                            if (score > bestScore) { bestScore = score; bestId = id; }
                        }
                    }
                }
                catch { /* malformed search response — try the next term/scope */ }
            }

            if (bestId is not null && bestScore >= 0.6)
                return bestId; // resolved within the DOS scope; no need to widen to all systems
        }
        return bestId is not null && bestScore >= 0.6 ? bestId : null;
    }

    // All textual names a jeuRecherche result carries (regional names array + a flat "nom").
    private static IEnumerable<string> GameNames(JsonNode jeu)
    {
        if (AsString(jeu["nom"]) is { } flat)
            yield return flat;
        if (jeu["noms"]?.AsArray() is { } noms)
            foreach (var item in noms)
            {
                if (AsString(item) is { } direct)
                    yield return direct;
                else if (item is JsonObject obj)
                    foreach (var kv in obj)
                        if ((kv.Key == "text" || kv.Key.StartsWith("nom", StringComparison.Ordinal))
                            && AsString(kv.Value) is { } t)
                            yield return t;
            }
    }

    private static string? AsString(JsonNode? node) =>
        node is JsonValue v && v.TryGetValue<string>(out var s) && !string.IsNullOrWhiteSpace(s) ? s : null;

    // Lowercase, numeral-normalized (Roman->Arabic so "V" == "5"), "&"->"and", accents folded
    // (café == cafe), alphanumerics only — so minor spelling/punctuation differences compare equal.
    private static string NormalizeForCompare(string name)
    {
        var s = SwapNumeralStyle(EpisodeToNumber(StripNoise(name)), toRoman: false).Replace("&", " and ").ToLowerInvariant();
        s = new string(s.Normalize(System.Text.NormalizationForm.FormD)
            .Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c)
                        != System.Globalization.UnicodeCategory.NonSpacingMark)
            .ToArray());
        return new string(s.Where(char.IsLetterOrDigit).ToArray());
    }

    // Best of common-prefix ratio and edit-distance ratio — prefix rewards "policequest" matching
    // "policequestinpursuit…", edit-distance rewards near-equal full names.
    private static double NameScore(string a, string b)
    {
        if (a.Length == 0 || b.Length == 0)
            return 0;
        int prefix = 0;
        while (prefix < a.Length && prefix < b.Length && a[prefix] == b[prefix])
            prefix++;
        double prefixScore = (double)prefix / Math.Min(a.Length, b.Length);
        double editScore = 1.0 - (double)Levenshtein(a, b) / Math.Max(a.Length, b.Length);
        return Math.Max(prefixScore, editScore);
    }

    // Trailing sequel number of a normalized name ("policequest2thevengeance" -> "2"), or null.
    private static string? LastNumber(string normalized)
    {
        var m = Regex.Matches(normalized, @"\d+");
        return m.Count > 0 ? m[^1].Value : null;
    }

    // Sequel-number agreement as a score multiplier: exact match (or both unnumbered) is best; an
    // unnumbered side vs a "1" is the lenient first-game case (still good, but loses to an exact
    // numbered match so "Keen 1" beats unnumbered "Keen: <other subtitle>"); anything else conflicts.
    private static double NumberMultiplier(string? a, string? b)
    {
        if (a == b)
            return 1.0;
        if ((a is null && b == "1") || (b is null && a == "1"))
            return 0.9;
        return 0.4;
    }

    // Safe to auto-adopt ScreenScraper's name: a close match AND a compatible sequel number, so a
    // fuzzy or wrong-sequel hit ("Police Quest 1" -> "Police Quest 2") never renames the game.
    private static bool IsConfidentRename(string wanted, string canonical)
    {
        var w = NormalizeForCompare(wanted);
        return w.Length > 0 && BestScore(w, canonical) >= 0.85;
    }

    // Best similarity of the wanted (already-normalized) title against a candidate name AND its
    // pre-subtitle base, minus a penalty for a conflicting sequel number. Scoring the base too means a
    // single missing letter or apostrophe still matches when SS appends a subtitle — e.g.
    // "King Quest 4" vs "King's Quest IV: The Perils of Rosella" (edit distance 1 against the base).
    private static double BestScore(string wantedNorm, string candidateName)
    {
        double best = 0;
        foreach (var raw in new[] { candidateName, BeforeSubtitle(candidateName) })
        {
            var variant = NormalizeForCompare(raw);
            if (variant.Length == 0)
                continue;
            var score = NameScore(wantedNorm, variant) * NumberMultiplier(LastNumber(wantedNorm), LastNumber(variant));
            if (score > best)
                best = score;
        }
        return best;
    }

    private static string BeforeSubtitle(string name)
    {
        int colon = name.IndexOf(':');
        if (colon > 0)
            return name[..colon];
        var dash = Regex.Match(name, @"\s[-–]\s");
        return dash.Success ? name[..dash.Index] : name;
    }

    private static int Levenshtein(string a, string b)
    {
        var d = new int[a.Length + 1, b.Length + 1];
        for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (int j = 0; j <= b.Length; j++) d[0, j] = j;
        for (int i = 1; i <= a.Length; i++)
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
            }
        return d[a.Length, b.Length];
    }

    /// <summary>Title-match candidates for ScreenScraper, most-specific first: the exact title, then
    /// the title with import noise stripped (years, region/publisher parentheses, [tags], version
    /// numbers — e.g. "Champions of Krynn v1.2 [a1] (1990)(SSI) [RPG]" → "Champions of Krynn"), then
    /// that with episode/collection words removed too. Distinct, non-empty.</summary>
    private static IEnumerable<string> NameCandidates(string name)
    {
        var bare = StripNoise(name);
        var candidates = new[]
        {
            name,                                   // exact
            bare,                                    // noise stripped
            HyphensToSpaces(bare),                   // "Lotus-The-Ultimate-Challenge" -> spaces
            EpisodeToNumber(bare),                   // "Commander Keen Episode 3" -> "Commander Keen 3"
            Possessive(bare),                        // "Kings Quest 6" -> "King's Quest 6"
            SwapNumeralStyle(Possessive(bare), toRoman: true), // "Kings Quest 6" -> "King's Quest VI"
            SwapNumeralStyle(bare, toRoman: true),   // "Dungeon Master 2" -> "II"
            SwapNumeralStyle(bare, toRoman: false),  // "Ishar II" -> "2"
            BaseTitle(bare),                         // "Carmageddon - High Octane" -> "Carmageddon"
            StripArticle(bare),                      // "The 7th Guest" -> "7th Guest" (and the reverse miss)
            DropTrailingNumber(bare),                // "Duke Nukem 1" / "Dark Ages 1" -> base (tried late)
            CleanTitle(bare),                        // episode/collection words removed
        };

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates)
        {
            var t = candidate.Trim().Trim('-', ':', '–', ',').Trim();
            if (t.Length >= 2 && seen.Add(t))
                yield return t;
        }
    }

    // The base game title before an edition/subtitle separator (" - " or ": "), e.g. an edition like
    // "Carmageddon - High Octane" that ScreenScraper only lists under the base name "Carmageddon".
    private static string BaseTitle(string name)
    {
        var parts = SubtitleRegex().Split(name);
        return parts.Length > 1 && parts[0].Trim().Length >= 3 ? parts[0].Trim() : name;
    }

    // ScreenScraper's fuzzy matcher doesn't bridge Arabic<->Roman sequel numerals, so we try both.
    private static readonly Dictionary<string, string> ArabicToRoman = new()
    {
        ["1"] = "I", ["2"] = "II", ["3"] = "III", ["4"] = "IV", ["5"] = "V",
        ["6"] = "VI", ["7"] = "VII", ["8"] = "VIII", ["9"] = "IX", ["10"] = "X",
    };
    private static readonly Dictionary<string, string> RomanToArabic = new(StringComparer.OrdinalIgnoreCase)
    {
        ["i"] = "1", ["ii"] = "2", ["iii"] = "3", ["iv"] = "4", ["v"] = "5",
        ["vi"] = "6", ["vii"] = "7", ["viii"] = "8", ["ix"] = "9", ["x"] = "10",
    };

    private static string SwapNumeralStyle(string input, bool toRoman) => toRoman
        ? Regex.Replace(input, @"\b(10|[1-9])\b",
            m => ArabicToRoman.TryGetValue(m.Value, out var r) ? r : m.Value)
        : Regex.Replace(input, @"\b(viii|vii|iii|ix|iv|vi|ii|x|v|i)\b",
            m => RomanToArabic.TryGetValue(m.Value, out var a) ? a : m.Value, RegexOptions.IgnoreCase);

    // Remove (...) and [...] groups (years, regions, publishers, dump flags — including nested ones)
    // and version tokens; the remaining bare title is what ScreenScraper matches best.
    private static string StripNoise(string name)
    {
        var s = name.Replace('_', ' '); // underscores are separators (e.g. "..._DOS_EN")
        for (int i = 0; i < 4 && ParenBracketRegex().IsMatch(s); i++)
            s = ParenBracketRegex().Replace(s, " ");
        s = VersionRegex().Replace(s, " ");
        s = TrailingTagRegex().Replace(s, ""); // trailing platform/language tags ("DOS", "EN", ...)
        s = LetterDigitRegex().Replace(s, "$1 $2"); // "Prince of Persia2" -> "Prince of Persia 2"
        s = Regex.Replace(s, @"\s+", " ").Trim().Trim('-', ':', '–', ',', '.').Trim();
        return s;
    }

    // Hyphen-separated titles ("Lotus-The-Ultimate-Challenge") — a fallback candidate, so intra-word
    // hyphens (e.g. "X-COM") still match their exact form first.
    private static string HyphensToSpaces(string name) =>
        name.Contains('-') ? Regex.Replace(name.Replace('-', ' '), @"\s+", " ").Trim() : name;

    // Leading article — ScreenScraper sometimes lists "The X" while our title omits it (or vice-versa).
    private static string StripArticle(string name) =>
        Regex.Replace(name, @"^(the|a|an)\s+", "", RegexOptions.IgnoreCase);

    // A trailing shareware episode number ("Duke Nukem 1", "Dark Ages 1") that SS lists without — tried
    // late, so true sequels still match their own numbered entry first.
    private static string DropTrailingNumber(string name) =>
        Regex.Replace(name, @"\s+\d{1,2}$", "");

    // "Episode N" / "Part N" / "Disk N" -> just "N" (SS lists "Commander Keen Episode 3" as
    // "Commander Keen 3"); keeps the number, drops the word.
    private static string EpisodeToNumber(string name) =>
        EpisodeNumRegex().Replace(name, "$1");

    // Possessive apostrophe on the first word ("Kings Quest" -> "King's Quest") — SS's search needs it
    // for series like King's Quest. A best-effort extra candidate; wrong guesses just don't match.
    private static string Possessive(string name) =>
        Regex.Replace(name, @"^(\w+)s\b", "$1's");

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

    // (...) and [...] groups — years, regions, publishers, dump/version flags.
    [GeneratedRegex(@"\([^()]*\)|\[[^\[\]]*\]")]
    private static partial Regex ParenBracketRegex();

    // Version tokens like v1.2, 1.021, 475.01.
    [GeneratedRegex(@"\bv?\d+(\.\d+)+\b", RegexOptions.IgnoreCase)]
    private static partial Regex VersionRegex();

    // An edition/subtitle separator: " - ", " – ", or " : ".
    [GeneratedRegex(@"\s+[-–:]\s+")]
    private static partial Regex SubtitleRegex();

    // "Episode 3" / "Ep 3" / "Part 3" / "Disk 3" -> captures the number for EpisodeToNumber.
    [GeneratedRegex(@"\b(?:episode|ep|chapter|part|disk|disc|volume|vol)\s*(\d+)\b", RegexOptions.IgnoreCase)]
    private static partial Regex EpisodeNumRegex();

    // Trailing platform/language tags left by some dumps ("... DOS EN").
    [GeneratedRegex(@"(\s+(dos|cd|en|eng|de|fr|es|usa|eur|world|wor))+$", RegexOptions.IgnoreCase)]
    private static partial Regex TrailingTagRegex();

    // A letter immediately followed by a digit ("Persia2") — split so sequel numbers stand alone.
    [GeneratedRegex(@"([A-Za-z])([0-9])")]
    private static partial Regex LetterDigitRegex();
}
