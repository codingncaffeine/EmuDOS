namespace EmuDOS.Metadata;

/// <summary>
/// Fetches and stores box art for a game into its gamebox media folder. ScreenScraper is the
/// primary source; SteamGridDB (when configured) is the fallback for games it doesn't have.
/// </summary>
public sealed class ArtService(ScreenScraperClient screenScraper, SteamGridDbClient? steamGridDb = null)
{
    public const string BoxFrontFileName = "box-front.png";

    /// <summary>The 3D box render, stored alongside the 2D <see cref="BoxFrontFileName"/>.</summary>
    public const string Box3DFileName = "box-3d.png";

    /// <summary>
    /// Fetch a game's box cover and save it as <c>box-front.png</c> in <paramref name="mediaDir"/>.
    /// Returns the saved path, or null if no art was found from any source.
    /// </summary>
    public async Task<string?> FetchBoxFrontAsync(
        string gameName, string mediaDir, CancellationToken cancellationToken = default)
    {
        var bytes = await FromScreenScraperAsync(gameName, cancellationToken);
        if (!IsUsable(bytes) && steamGridDb is not null)
            bytes = await FromSteamGridDbAsync(gameName, cancellationToken);

        if (!IsUsable(bytes))
            return null;

        Directory.CreateDirectory(mediaDir);
        var path = Path.Combine(mediaDir, BoxFrontFileName);
        await File.WriteAllBytesAsync(path, bytes!, cancellationToken);
        return path;
    }

    /// <summary>
    /// Fetch a game's 3D box render and save it as <c>box-3d.png</c> in <paramref name="mediaDir"/>.
    /// ScreenScraper-only (no SteamGridDB 3D). Returns the saved path, or null if none was found.
    /// </summary>
    public async Task<string?> FetchBox3DAsync(
        string gameName, string mediaDir, CancellationToken cancellationToken = default)
    {
        var url = await screenScraper.FindBox3DUrlAsync(gameName, cancellationToken);
        var bytes = url is null ? null : await screenScraper.DownloadAsync(url, cancellationToken);
        if (!IsUsable(bytes))
            return null;

        Directory.CreateDirectory(mediaDir);
        var path = Path.Combine(mediaDir, Box3DFileName);
        await File.WriteAllBytesAsync(path, bytes!, cancellationToken);
        return path;
    }

    private async Task<byte[]?> FromScreenScraperAsync(string gameName, CancellationToken cancellationToken)
    {
        var url = await screenScraper.FindBoxArtUrlAsync(gameName, cancellationToken);
        return url is null ? null : await screenScraper.DownloadAsync(url, cancellationToken);
    }

    private async Task<byte[]?> FromSteamGridDbAsync(string gameName, CancellationToken cancellationToken)
    {
        var url = await steamGridDb!.FindBoxArtUrlAsync(gameName, cancellationToken);
        return url is null ? null : await steamGridDb.DownloadAsync(url, cancellationToken);
    }

    private static bool IsUsable(byte[]? bytes) => bytes is { Length: > 1000 };
}
