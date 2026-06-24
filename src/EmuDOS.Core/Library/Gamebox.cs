namespace EmuDOS.Core.Library;

/// <summary>
/// The on-disk layout of a gamebox — a self-contained game folder that is the source of
/// truth (the library DB is only a rebuildable index over these). Backing up or moving the
/// folder moves the whole game: its config, content, media, and saves.
/// </summary>
public sealed class Gamebox(string root)
{
    public string Root { get; } = root;

    /// <summary>Canonical <c>GameProfile</c> as JSON.</summary>
    public string ProfilePath => Path.Combine(Root, "profile.json");

    /// <summary>Game files, mounted as C:. The generated DOSBOX.BAT is written here at launch.</summary>
    public string ContentDir => Path.Combine(Root, "content");

    /// <summary>Box art and manuals.</summary>
    public string MediaDir => Path.Combine(Root, "media");

    /// <summary>Screenshots captured in-game (per-game, under media/).</summary>
    public string ScreenshotsDir => Path.Combine(Root, "media", "screenshots");

    /// <summary>Recorded videos (per-game, under media/).</summary>
    public string VideosDir => Path.Combine(Root, "media", "videos");

    /// <summary>Free-form per-game notes (plain text/markdown), travels with the gamebox.</summary>
    public string NotesPath => Path.Combine(Root, "notes.md");

    /// <summary>Descriptive metadata (genre, year, developer, …) as JSON; shown on the game card.</summary>
    public string MetadataPath => Path.Combine(Root, "metadata.json");

    /// <summary>Save data and save states.</summary>
    public string SavesDir => Path.Combine(Root, "saves");

    /// <summary>Downloaded extras (logos, marquees, fanart, maps, screenshots) for the game.</summary>
    public string ExtrasDir => Path.Combine(Root, "media", "extras");

    /// <summary>Per-game user state (window size, remembered executables) as JSON.</summary>
    public string StatePath => Path.Combine(Root, "state.json");

    /// <summary>True if this folder holds a profile (i.e. is a gamebox).</summary>
    public bool Exists => File.Exists(ProfilePath);
}
