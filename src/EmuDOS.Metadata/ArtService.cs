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

    /// <summary>Fetch descriptive metadata (genre, year, developer, …) from ScreenScraper, or null.</summary>
    public Task<Core.Model.GameMetadata?> FetchMetadataAsync(string gameName, CancellationToken cancellationToken = default)
        => screenScraper.FetchMetadataAsync(gameName, cancellationToken);

    /// <summary>Resolve a lookup term to ScreenScraper's canonical game name (for the manual rename), or null.</summary>
    public Task<string?> ResolveNameAsync(string gameName, CancellationToken cancellationToken = default)
        => screenScraper.ResolveNameAsync(gameName, cancellationToken);

    /// <summary>Fetch a game's gameplay video snap from ScreenScraper and save it to
    /// <paramref name="destPath"/> (an .mp4). Returns true on success. ScreenScraper-only — SteamGridDB
    /// has no video.</summary>
    public async Task<bool> FetchSnapAsync(string gameName, string destPath, CancellationToken cancellationToken = default)
    {
        var url = await screenScraper.FindVideoUrlAsync(gameName, cancellationToken);
        var bytes = url is null ? null : await screenScraper.DownloadAsync(url, cancellationToken);
        if (bytes is not { Length: > 1000 })
            return false;

        Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
        await File.WriteAllBytesAsync(destPath, bytes, cancellationToken);
        return true;
    }

    /// <summary>Download the available extras (clear logo, marquee, fanart, screenshot, maps) for a
    /// game into <paramref name="extrasDir"/>, named <c>&lt;type&gt;.&lt;ext&gt;</c>. Returns how many
    /// were saved. ScreenScraper-only.</summary>
    public async Task<int> FetchExtrasAsync(string gameName, string extrasDir, CancellationToken cancellationToken = default)
    {
        var extras = await screenScraper.FindExtrasAsync(gameName, cancellationToken);
        if (extras.Count == 0)
            return 0;

        Directory.CreateDirectory(extrasDir);
        int saved = 0;
        foreach (var (type, url, format) in extras)
        {
            var bytes = await screenScraper.DownloadAsync(url, cancellationToken);
            if (!IsUsable(bytes))
                continue;
            await File.WriteAllBytesAsync(Path.Combine(extrasDir, $"{type}.{format}"), bytes!, cancellationToken);
            saved++;
        }
        return saved;
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
