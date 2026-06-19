using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;

namespace EmuDOS.Metadata;

/// <summary>
/// Minimal SteamGridDB API client — a fallback art source for games ScreenScraper doesn't
/// have. Uses 600×900 "grids", which are portrait box-art-shaped, matching the shelf.
/// </summary>
public sealed class SteamGridDbClient(HttpClient http, string apiKey)
{
    private const string BaseUrl = "https://www.steamgriddb.com/api/v2/";

    /// <summary>True if the API key is accepted by SteamGridDB.</summary>
    public async Task<bool> ValidateKeyAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            return false;

        try
        {
            using var response = await Get($"search/autocomplete/doom", cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Find a portrait box-art URL for a game by name, or null.</summary>
    public async Task<string?> FindBoxArtUrlAsync(string gameName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(gameName))
            return null;

        try
        {
            var gameId = await SearchGameIdAsync(gameName, cancellationToken);
            if (gameId is null)
                return null;

            using var response = await Get(
                $"grids/game/{gameId}?dimensions=600x900&types=static", cancellationToken);
            if (!response.IsSuccessStatusCode)
                return null;

            var data = JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken))?["data"]?.AsArray();
            return data?.FirstOrDefault()?["url"]?.GetValue<string>();
        }
        catch
        {
            return null;
        }
    }

    public async Task<byte[]?> DownloadAsync(string url, CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await http.GetAsync(url, cancellationToken);
            return response.IsSuccessStatusCode
                ? await response.Content.ReadAsByteArrayAsync(cancellationToken)
                : null;
        }
        catch
        {
            return null;
        }
    }

    private async Task<int?> SearchGameIdAsync(string name, CancellationToken cancellationToken)
    {
        using var response = await Get($"search/autocomplete/{Uri.EscapeDataString(name)}", cancellationToken);
        if (!response.IsSuccessStatusCode)
            return null;

        var data = JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken))?["data"]?.AsArray();
        return data is { Count: > 0 } ? data[0]?["id"]?.GetValue<int>() : null;
    }

    private Task<HttpResponseMessage> Get(string path, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, BaseUrl + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        return http.SendAsync(request, cancellationToken);
    }
}
