namespace EmuDOS.Core.Downloads;

/// <summary>
/// The catalogue of downloadable third-party assets. dosbox_pure is fetched from the
/// libretro nightly buildbot (as RetroArch/Emutastic do) rather than bundled, to avoid
/// shipping a GPL-licensed binary in our distribution.
/// </summary>
public static class AssetManifest
{
    public const string DosBoxPureFileName = "dosbox_pure_libretro.dll";
    public const string CatalogFileName = "catalog.db";
    public const string Mt32ShimFileName = "emudos_mt32.dll";

    private const string ReleaseBase = "https://github.com/codingncaffeine/EmuDOS/releases/latest/download";

    /// <summary>The dosbox_pure libretro core (Windows x86-64).</summary>
    public static DownloadAsset DosBoxPure { get; } = new()
    {
        Id = "dosbox_pure",
        DisplayName = "DOSBox Pure core",
        Description = "The DOS emulator that runs your games. Required.",
        Url = "https://buildbot.libretro.com/nightly/windows/x86_64/latest/dosbox_pure_libretro.dll.zip",
        Kind = DownloadKind.ZippedCore,
        FileName = DosBoxPureFileName,
        Category = AssetCategory.Core,
    };

    /// <summary>The curated config catalog (recognizes games and applies good settings on import).</summary>
    public static DownloadAsset Catalog { get; } = new()
    {
        Id = "catalog",
        DisplayName = "Game catalog",
        Description = "Recognizes imported games and applies curated settings. Recommended.",
        Url = $"{ReleaseBase}/{CatalogFileName}",
        Kind = DownloadKind.File,
        FileName = CatalogFileName,
        Category = AssetCategory.Catalog,
    };

    /// <summary>Our MT-32 synth shim — enables Roland MT-32 music + the LCD when ROMs are present.</summary>
    public static DownloadAsset Mt32Shim { get; } = new()
    {
        Id = "mt32_shim",
        DisplayName = "MT-32 sound module",
        Description = "Roland MT-32 music and LCD (also needs the MT-32 ROMs). Optional.",
        Url = $"{ReleaseBase}/{Mt32ShimFileName}",
        Kind = DownloadKind.File,
        FileName = Mt32ShimFileName,
        Category = AssetCategory.Native,
    };

    /// <summary>All assets the Downloads tab can offer.</summary>
    public static IReadOnlyList<DownloadAsset> All { get; } = [DosBoxPure, Catalog, Mt32Shim];
}
