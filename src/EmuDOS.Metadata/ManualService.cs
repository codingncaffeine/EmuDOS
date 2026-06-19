namespace EmuDOS.Metadata;

/// <summary>Downloads a game's manual into a per-game folder, trying ScreenScraper first and the
/// Internet Archive as a fallback. The file is saved by its real type (sniffed), never as the
/// server's <c>.php</c> endpoint name.</summary>
public sealed class ManualService(ScreenScraperClient screenScraper, ArchiveOrgManualClient archive)
{
    /// <summary>
    /// Fetch the manual for <paramref name="gameName"/> into <paramref name="destDir"/>.
    /// Returns the saved file path, or null if no manual was found anywhere.
    /// </summary>
    public async Task<string?> FetchManualAsync(
        string gameName, string destDir, CancellationToken cancellationToken = default)
    {
        var url = await screenScraper.FindManualUrlAsync(gameName, cancellationToken)
                  ?? await archive.FindManualPdfUrlAsync(gameName, cancellationToken);
        if (url is null)
            return null;

        var bytes = await screenScraper.DownloadAsync(url, cancellationToken);
        if (bytes is null || bytes.Length < 1024)
            return null;

        Directory.CreateDirectory(destDir);
        var path = Path.Combine(destDir, "manual" + ExtensionFor(bytes));
        await File.WriteAllBytesAsync(path, bytes, cancellationToken);
        return path;
    }

    // Manuals come back through a .php endpoint, so trust the bytes, not the URL. Default to .pdf
    // (what manuals almost always are) rather than ever leaving a confusing .php on disk.
    private static string ExtensionFor(byte[] b)
    {
        if (b.Length >= 4 && b[0] == 0x25 && b[1] == 0x50 && b[2] == 0x44 && b[3] == 0x46)
            return ".pdf"; // %PDF
        if (b.Length >= 2 && b[0] == 0x50 && b[1] == 0x4B)
            return ".zip"; // PK (zip)
        if (b.Length >= 4 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47)
            return ".png";
        if (b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF)
            return ".jpg";
        return ".pdf";
    }
}
