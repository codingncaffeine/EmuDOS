namespace EmuDOS.Core.Infrastructure;

/// <summary>User-configurable settings (accounts, preferences) persisted to the data folder.</summary>
public sealed class UserSettings
{
    /// <summary>ScreenScraper.fr account (raises art quotas above anonymous dev-cred access).</summary>
    public string ScreenScraperUser { get; set; } = string.Empty;

    public string ScreenScraperPassword { get; set; } = string.Empty;

    /// <summary>SteamGridDB API key (used as an art fallback source).</summary>
    public string SteamGridDbKey { get; set; } = string.Empty;

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
}
