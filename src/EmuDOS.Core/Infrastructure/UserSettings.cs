namespace EmuDOS.Core.Infrastructure;

/// <summary>User-configurable settings (accounts, preferences) persisted to the data folder.</summary>
public sealed class UserSettings
{
    /// <summary>ScreenScraper.fr account (raises art quotas above anonymous dev-cred access).</summary>
    public string ScreenScraperUser { get; set; } = string.Empty;

    public string ScreenScraperPassword { get; set; } = string.Empty;

    /// <summary>SteamGridDB API key (used as an art fallback source).</summary>
    public string SteamGridDbKey { get; set; } = string.Empty;
}
