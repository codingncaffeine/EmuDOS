namespace EmuDOS.Core.Infrastructure;

/// <summary>User-configurable settings (accounts, preferences) persisted to the data folder.</summary>
public sealed class UserSettings
{
    /// <summary>ScreenScraper.fr account (raises art quotas above anonymous dev-cred access).</summary>
    public string ScreenScraperUser { get; set; } = string.Empty;

    public string ScreenScraperPassword { get; set; } = string.Empty;

    /// <summary>Concurrent ScreenScraper requests the account may make (its <c>maxthreads</c>, captured
    /// at login). Caps bulk art downloads; 1 for free/anonymous, more for paid tiers (server-enforced).</summary>
    public int ScreenScraperMaxThreads { get; set; } = 1;

    /// <summary>SteamGridDB API key (used as an art fallback source).</summary>
    public string SteamGridDbKey { get; set; } = string.Empty;

    /// <summary>Global default box-art style: true = show 3D boxes (box-3d.png) where available,
    /// false = flat 2D covers. Per-game overrides (state.json BoxStyle) win over this.</summary>
    public bool Use3DBoxes { get; set; }

    // --- Media (screenshots + recording). Empty folder = use the AppPaths default. ---

    /// <summary>Where screenshots are saved (empty = the default Screenshots folder).</summary>
    public string ScreenshotFolder { get; set; } = string.Empty;

    /// <summary>Where recorded videos are saved (empty = the default Videos folder).</summary>
    public string VideoFolder { get; set; } = string.Empty;

    /// <summary>True = save screenshots at the game's native resolution (pixel-perfect);
    /// false = at the window/displayed size.</summary>
    public bool ScreenshotOriginalSize { get; set; } = true;

    /// <summary>Video recording quality: "Low", "Medium", or "High".</summary>
    public string VideoQuality { get; set; } = "Medium";

    // --- Hotkeys (WPF Key names; remappable in Preferences → Hotkeys). ---

    /// <summary>Key that captures a screenshot.</summary>
    public string ScreenshotKey { get; set; } = "F12";

    /// <summary>Key that starts/stops video recording.</summary>
    public string RecordKey { get; set; } = "F9";

    /// <summary>Optional key that toggles the mouse lock (middle-click always toggles it too).
    /// Empty means middle-click only.</summary>
    public string MouseLockKey { get; set; } = string.Empty;

    /// <summary>Key that opens dosbox's in-game menu (for swapping CDs/disks, on-screen keyboard).</summary>
    public string MenuKey { get; set; } = "F10";

    /// <summary>Key that writes a quick save state for the running game.</summary>
    public string SaveStateKey { get; set; } = "F5";

    /// <summary>Key that loads the quick save state for the running game.</summary>
    public string LoadStateKey { get; set; } = "F8";

    /// <summary>Key that opens the in-game cheat engine.</summary>
    public string CheatKey { get; set; } = "F11";

    /// <summary>Hold to fast-forward (run faster than real time). Default-safe vs game keys; remappable.</summary>
    public string FastForwardKey { get; set; } = "F6";

    /// <summary>Hold to slow the game down.</summary>
    public string SlowMotionKey { get; set; } = "F7";

    /// <summary>Toggle pause/resume of the running game.</summary>
    public string PauseKey { get; set; } = "Pause";

    /// <summary>Hold to rewind the game through recently captured states.</summary>
    public string RewindKey { get; set; } = "F4";

    /// <summary>Cycle the in-game CRT video shader (Off / Scanlines / CRT).</summary>
    public string ShaderCycleKey { get; set; } = "F3";

    /// <summary>In-game key that toggles the FPS overlay (current vs. locked frame rate).</summary>
    public string FpsOverlayKey { get; set; } = "F1";

    /// <summary>The CRT video shader applied to games: "Off", "Scanlines", or "Crt".</summary>
    public string VideoShader { get; set; } = "Off";

    /// <summary>Render 3dfx/Voodoo games through hardware OpenGL (sharper, scalable). When off, the
    /// core falls back to its software Voodoo renderer. Non-3dfx games are unaffected either way.</summary>
    public bool Hardware3dfx { get; set; } = true;

    // --- Cloud save sync (GitHub). ---

    /// <summary>GitHub OAuth access token from the device-flow login (empty = not connected).</summary>
    public string GitHubToken { get; set; } = string.Empty;

    /// <summary>The connected GitHub account's login name (for display).</summary>
    public string GitHubLogin { get; set; } = string.Empty;

    /// <summary>The private repo that holds synced saves.</summary>
    public string GitHubRepo { get; set; } = "emudos-saves";

    /// <summary>Optional passphrase to encrypt cloud-synced save data (empty = no encryption). Protects
    /// the copy stored in the cloud repo; the same passphrase is needed on every PC that syncs.</summary>
    public string CloudEncryptionPassphrase { get; set; } = string.Empty;

    // --- Updates. ---

    /// <summary>Check GitHub for a newer release on startup and surface it in the status bar.</summary>
    public bool CheckForUpdates { get; set; } = true;
}
