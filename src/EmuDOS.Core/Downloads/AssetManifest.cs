namespace EmuDOS.Core.Downloads;

/// <summary>
/// The catalogue of downloadable third-party assets. dosbox_pure is fetched from the
/// libretro nightly buildbot (as RetroArch/Emutastic do) rather than bundled, to avoid
/// shipping a GPL-licensed binary in our distribution.
/// </summary>
public static class AssetManifest
{
    public const string DosBoxPureFileName = "dosbox_pure_libretro.dll";

    /// <summary>The dosbox_pure libretro core (Windows x86-64).</summary>
    public static DownloadAsset DosBoxPure { get; } = new()
    {
        Id = "dosbox_pure",
        DisplayName = "DOSBox Pure core",
        Url = "https://buildbot.libretro.com/nightly/windows/x86_64/latest/dosbox_pure_libretro.dll.zip",
        Kind = DownloadKind.ZippedCore,
        FileName = DosBoxPureFileName,
        Category = AssetCategory.Core,
    };

    /// <summary>All assets the Downloads tab can offer.</summary>
    public static IReadOnlyList<DownloadAsset> All { get; } = [DosBoxPure];
}
