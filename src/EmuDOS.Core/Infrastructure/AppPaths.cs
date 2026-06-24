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

    /// <summary>Downloaded game manuals, organized into a sub-folder per game.</summary>
    public string ManualsDir => Path.Combine(DataRoot, "Manuals");

    /// <summary>Downloaded curated config database updates (override the embedded baseline).</summary>
    public string CatalogDir => Path.Combine(DataRoot, "Catalog");

    /// <summary>Default location for screenshots (overridable in Preferences → Media).</summary>
    public string ScreenshotsDir => Path.Combine(DataRoot, "Screenshots");

    /// <summary>Default location for recorded videos (overridable in Preferences → Media).</summary>
    public string VideosDir => Path.Combine(DataRoot, "Videos");

    /// <summary>Cached gameplay video snaps for the game card, keyed by game identity. Retained — kept
    /// when a game is deleted so re-importing (e.g. a different version) reuses it, like box art.</summary>
    public string SnapsDir => Path.Combine(DataRoot, "Snaps");

    /// <summary>Downloaded libretro slang shader pack + librashader.dll (CRT shaders).</summary>
    public string ShadersDir => Path.Combine(DataRoot, "Shaders");
    /// <summary>Root of the extracted slang shader pack (under <see cref="ShadersDir"/>).</summary>
    public string SlangShaderRoot => Path.Combine(ShadersDir, "slang");
    /// <summary>The librashader runtime DLL path (under <see cref="ShadersDir"/>).</summary>
    public string LibrashaderDllPath => Path.Combine(ShadersDir, "librashader.dll");

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
