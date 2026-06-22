using System.Net.Http;
using EmuDOS.Core.Audio;
using EmuDOS.Core.Catalog;
using EmuDOS.Core.Downloads;
using EmuDOS.Core.Import;
using EmuDOS.Core.Infrastructure;
using EmuDOS.Core.Library;
using EmuDOS.Metadata;

namespace EmuDOS.Services;

/// <summary>
/// Composition root: constructs and holds the Core services for the app's lifetime. A plain
/// hand-wired graph — no container needed yet.
/// </summary>
public sealed class AppServices
{
    private readonly HttpClient _screenScraperHttp;

    public AppServices()
    {
        Paths = new AppPaths();
        SettingsStore = new SettingsStore(Paths);
        Settings = SettingsStore.Load();
        Store = new GameboxStore();
        Library = new LibraryDatabase(Paths, Store);
        ArtCache = new ArtCache(Paths);
        SystemFiles = new SystemFileInstaller(Paths);
        Catalog = new CatalogDatabase(System.IO.Path.Combine(Paths.CatalogDir, "catalog.db"));
        Resolver = new ProfileResolver(Catalog);
        Import = new ImportPipeline(Paths, Store, Resolver);
        Downloads = new DownloadService(new HttpClient(), Paths);

        _screenScraperHttp = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        _screenScraperHttp.DefaultRequestHeaders.Add("User-Agent", "EmuDOS/1.0");
        SnapsLog = new AppLog(Paths, "snaps.log");
        SystemLog = new AppLog(Paths, "system.log");
        CloudLog = new AppLog(Paths, "cloud-sync.log");
        Art = BuildArtService();
    }

    public AppPaths Paths { get; }

    public AppLog SnapsLog { get; }

    public AppLog SystemLog { get; }

    public AppLog CloudLog { get; }

    public UserSettings Settings { get; }

    public SettingsStore SettingsStore { get; }

    public GameboxStore Store { get; }

    public LibraryDatabase Library { get; }

    public ArtCache ArtCache { get; }

    public SystemFileInstaller SystemFiles { get; }

    public CatalogDatabase Catalog { get; }

    public ProfileResolver Resolver { get; }

    public ImportPipeline Import { get; }

    public DownloadService Downloads { get; }

    public ArtService Art { get; private set; }

    public ManualService Manuals { get; private set; } = null!;

    /// <summary>Rebuild the art service after the ScreenScraper login changes.</summary>
    public void ReloadArtService() => Art = BuildArtService();

    /// <summary>Verify a ScreenScraper login; logs the result. Returns success and the account's
    /// concurrent-request (maxthreads) allowance.</summary>
    public async Task<(bool Ok, int MaxThreads)> ValidateScreenScraperAsync(string user, string password)
    {
        var result = await new ScreenScraperClient(_screenScraperHttp, user, password).ValidateLoginAsync();
        SnapsLog.Info($"ScreenScraper login test for '{user}': {(result.Ok ? "SUCCESS" : "FAILED")} (maxthreads={result.MaxThreads})");
        return result;
    }

    /// <summary>Verify a SteamGridDB API key; logs the result.</summary>
    public async Task<bool> ValidateSteamGridDbAsync(string apiKey)
    {
        var ok = await new SteamGridDbClient(_screenScraperHttp, apiKey).ValidateKeyAsync();
        SnapsLog.Info($"SteamGridDB key test: {(ok ? "SUCCESS" : "FAILED")}");
        return ok;
    }

    private ArtService BuildArtService()
    {
        var screenScraper = new ScreenScraperClient(
            _screenScraperHttp, Settings.ScreenScraperUser, Settings.ScreenScraperPassword);
        var steamGridDb = string.IsNullOrWhiteSpace(Settings.SteamGridDbKey)
            ? null
            : new SteamGridDbClient(_screenScraperHttp, Settings.SteamGridDbKey);
        Manuals = new ManualService(screenScraper, new ArchiveOrgManualClient(_screenScraperHttp));
        return new ArtService(screenScraper, steamGridDb);
    }
}
