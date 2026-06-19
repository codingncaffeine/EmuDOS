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

    // Note: the MT-32 synth shim (emudos_mt32.dll) ships WITH the app — it's our own small
    // LGPL-based DLL, so unlike the GPL core there's no reason to download it. The only
    // user-supplied MT-32 piece is the Roland ROMs (copyrighted; detected, never distributed).

    /// <summary>All assets the Downloads tab can offer.</summary>
    public static IReadOnlyList<DownloadAsset> All { get; } = [DosBoxPure, Catalog];
}
