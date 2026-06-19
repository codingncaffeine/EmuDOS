namespace EmuDOS.Metadata;

/// <summary>Downloads a game's manual (from ScreenScraper) into a per-game folder.</summary>
public sealed class ManualService(ScreenScraperClient screenScraper)
{
    /// <summary>
    /// Fetch the manual for <paramref name="gameName"/> into <paramref name="destDir"/>.
    /// Returns the saved file path, or null if no manual was found.
    /// </summary>
    public async Task<string?> FetchManualAsync(
        string gameName, string destDir, CancellationToken cancellationToken = default)
    {
        var url = await screenScraper.FindManualUrlAsync(gameName, cancellationToken);
        if (url is null)
            return null;

        var bytes = await screenScraper.DownloadAsync(url, cancellationToken);
        if (bytes is null || bytes.Length < 1024)
            return null;

        Directory.CreateDirectory(destDir);
        var ext = Path.GetExtension(new Uri(url).AbsolutePath);
        if (string.IsNullOrWhiteSpace(ext))
            ext = ".pdf";

        var path = Path.Combine(destDir, "manual" + ext);
        await File.WriteAllBytesAsync(path, bytes, cancellationToken);
        return path;
    }
}
