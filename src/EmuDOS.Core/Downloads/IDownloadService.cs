namespace EmuDOS.Core.Downloads;

/// <summary>Fetches and installs third-party assets (cores, soundfonts, ROMs) on demand.</summary>
public interface IDownloadService
{
    /// <summary>Is the asset already present on disk?</summary>
    bool IsInstalled(DownloadAsset asset);

    /// <summary>Full path where the asset is (or would be) installed.</summary>
    string InstalledPath(DownloadAsset asset);

    /// <summary>Download and install an asset, reporting progress.</summary>
    Task<DownloadResult> DownloadAsync(
        DownloadAsset asset,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
