namespace EmuDOS.Core.Infrastructure;

/// <summary>
/// Resolves where EmuDOS keeps its data. Downloaded third-party assets (the dosbox_pure
/// core, soundfonts, ROMs) and user data (gameboxes, saves, library) live here — never in
/// the application directory. Portable mode can later point <see cref="DataRoot"/> at a
/// folder next to the exe.
/// </summary>
public sealed class AppPaths
{
    public AppPaths(string? dataRoot = null)
    {
        DataRoot = string.IsNullOrWhiteSpace(dataRoot) ? DefaultDataRoot() : dataRoot;
        EnsureDirectories();
    }

    public string DataRoot { get; }

    /// <summary>Downloaded libretro cores (e.g. dosbox_pure_libretro.dll).</summary>
    public string CoresDir => Path.Combine(DataRoot, "Cores");

    /// <summary>Core system files: SoundFonts, MT-32 ROMs, BIOS.</summary>
    public string SystemDir => Path.Combine(DataRoot, "System");

    /// <summary>Self-contained game folders (the source of truth).</summary>
    public string GameboxesDir => Path.Combine(DataRoot, "Gameboxes");

    /// <summary>Save data and save states.</summary>
    public string SavesDir => Path.Combine(DataRoot, "Saves");

    /// <summary>Downloaded curated config database updates (override the embedded baseline).</summary>
    public string CatalogDir => Path.Combine(DataRoot, "Catalog");

    private static string DefaultDataRoot() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EmuDOS");

    private void EnsureDirectories()
    {
        Directory.CreateDirectory(CoresDir);
        Directory.CreateDirectory(SystemDir);
        Directory.CreateDirectory(GameboxesDir);
        Directory.CreateDirectory(SavesDir);
        Directory.CreateDirectory(CatalogDir);
    }
}
