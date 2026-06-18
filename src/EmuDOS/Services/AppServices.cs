using System.Net.Http;
using EmuDOS.Core.Catalog;
using EmuDOS.Core.Downloads;
using EmuDOS.Core.Import;
using EmuDOS.Core.Infrastructure;
using EmuDOS.Core.Library;

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
    }

    public AppPaths Paths { get; }

    public GameboxStore Store { get; }

    public LibraryDatabase Library { get; }

    public CatalogDatabase Catalog { get; }

    public ProfileResolver Resolver { get; }

    public ImportPipeline Import { get; }

    public DownloadService Downloads { get; }
}
