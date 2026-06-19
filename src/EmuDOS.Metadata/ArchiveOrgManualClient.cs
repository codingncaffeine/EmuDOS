using System.Net.Http;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace EmuDOS.Metadata;

/// <summary>
/// A fallback manual source: the Internet Archive. ScreenScraper's manual coverage is patchy, so
/// when it has nothing we search archive.org's text items for a PDF whose title matches the game.
/// Conservative on purpose — every significant word of the title must appear, to avoid wrong hits.
/// </summary>
public sealed partial class ArchiveOrgManualClient(HttpClient http)
{
    public async Task<string?> FindManualPdfUrlAsync(string gameName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameName);

        var words = Significant(gameName);
        if (words.Count == 0)
            return null;

        var query = Uri.EscapeDataString($"({gameName}) AND mediatype:texts");
        var searchUrl =
            $"https://archive.org/advancedsearch.php?q={query}&fl[]=identifier&fl[]=title&rows=6&output=json";

        JsonNode? search;
        try { search = JsonNode.Parse(await http.GetStringAsync(searchUrl, cancellationToken)); }
        catch { return null; }

        var docs = search?["response"]?["docs"]?.AsArray();
        if (docs is null)
            return null;

        foreach (var doc in docs)
        {
            var id = doc?["identifier"]?.GetValue<string>();
            var title = doc?["title"]?.GetValue<string>() ?? string.Empty;
            if (id is null || !TitleMatches(title, words))
                continue;

            var pdf = await FirstPdfNameAsync(id, cancellationToken);
            if (pdf is not null)
                return $"https://archive.org/download/{id}/{Uri.EscapeDataString(pdf)}";
        }

        return null;
    }

    private async Task<string?> FirstPdfNameAsync(string identifier, CancellationToken cancellationToken)
    {
        try
        {
            var meta = JsonNode.Parse(await http.GetStringAsync(
                $"https://archive.org/metadata/{identifier}", cancellationToken));
            var files = meta?["files"]?.AsArray();
            if (files is null)
                return null;

            return files
                .Select(f => f?["name"]?.GetValue<string>())
                .FirstOrDefault(n => n is not null && n.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return null;
        }
    }

    private static List<string> Significant(string name) =>
        WordRegex().Matches(name.ToLowerInvariant())
            .Select(m => m.Value)
            .Where(w => w.Length > 1) // drop "a", "of", stray letters
            .ToList();

    private static bool TitleMatches(string title, List<string> words)
    {
        var t = title.ToLowerInvariant();
        return words.All(w => t.Contains(w, StringComparison.Ordinal));
    }

    [GeneratedRegex(@"[a-z0-9]+")]
    private static partial Regex WordRegex();
}
