namespace EmuDOS.Metadata;

/// <summary>Fetches and stores box art for a game into its gamebox media folder.</summary>
public sealed class ArtService(ScreenScraperClient client)
{
    public const string BoxFrontFileName = "box-front.png";

    /// <summary>
    /// Fetch a game's 2D box cover and save it as <c>box-front.png</c> in <paramref name="mediaDir"/>.
    /// Returns the saved path, or null if no art was found.
    /// </summary>
    public async Task<string?> FetchBoxFrontAsync(
        string gameName, string mediaDir, CancellationToken cancellationToken = default)
    {
        var url = await client.FindBoxArtUrlAsync(gameName, cancellationToken);
        if (url is null)
            return null;

        var bytes = await client.DownloadAsync(url, cancellationToken);
        if (bytes is null || bytes.Length < 1000)
            return null;

        Directory.CreateDirectory(mediaDir);
        var path = Path.Combine(mediaDir, BoxFrontFileName);
        await File.WriteAllBytesAsync(path, bytes, cancellationToken);
        return path;
    }
}
