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

    /// <summary>Box art, manuals, screenshots.</summary>
    public string MediaDir => Path.Combine(Root, "media");

    /// <summary>Save data and save states.</summary>
    public string SavesDir => Path.Combine(Root, "saves");

    /// <summary>True if this folder holds a profile (i.e. is a gamebox).</summary>
    public bool Exists => File.Exists(ProfilePath);
}
