using System.Net.Http;
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
    public AppServices()
    {
        Paths = new AppPaths();
        Store = new GameboxStore();
        Library = new LibraryDatabase(Paths, Store);
        Catalog = new CatalogDatabase(System.IO.Path.Combine(Paths.CatalogDir, "catalog.db"));
        Resolver = new ProfileResolver(Catalog);
        Import = new ImportPipeline(Paths, Store, Resolver);
        Downloads = new DownloadService(new HttpClient(), Paths);

        var ssHttp = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        ssHttp.DefaultRequestHeaders.Add("User-Agent", "EmuDOS/1.0");
        // Anonymous (dev-cred) access for now; the Accounts tab can supply a user login later.
        Art = new ArtService(new ScreenScraperClient(ssHttp, string.Empty, string.Empty));
    }

    public AppPaths Paths { get; }

    public GameboxStore Store { get; }

    public LibraryDatabase Library { get; }

    public CatalogDatabase Catalog { get; }

    public ProfileResolver Resolver { get; }

    public ImportPipeline Import { get; }

    public DownloadService Downloads { get; }

    public ArtService Art { get; }
}
