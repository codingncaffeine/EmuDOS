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

    // --- Cloud save sync (GitHub). ---

    /// <summary>GitHub OAuth access token from the device-flow login (empty = not connected).</summary>
    public string GitHubToken { get; set; } = string.Empty;

    /// <summary>The connected GitHub account's login name (for display).</summary>
    public string GitHubLogin { get; set; } = string.Empty;

    /// <summary>The private repo that holds synced saves.</summary>
    public string GitHubRepo { get; set; } = "emudos-saves";
}
