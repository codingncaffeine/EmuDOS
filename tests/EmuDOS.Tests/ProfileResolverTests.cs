using EmuDOS.Core.Catalog;
using EmuDOS.Core.Import;
using EmuDOS.Core.Infrastructure;
using EmuDOS.Core.Library;
using EmuDOS.Core.Model;

namespace EmuDOS.Tests;

public class ProfileResolverTests
{
    [Fact]
    public void Enriches_baseline_when_catalog_matches_but_keeps_identity()
    {
        var resolver = new ProfileResolver(Catalog(DoomEntry()));
        var baseline = new GameProfile
        {
            Title = "My Doom",
            Launch = new LaunchSpec { Executable = "DOOM.EXE" },
        };

        var result = resolver.Resolve(baseline, ["doom.exe", "doom.wad"]);

        Assert.Equal(ProfileOrigin.CuratedBase, result.Origin);
        Assert.Equal(20000, result.Cpu.FixedCycles); // curated config applied
        Assert.Equal("My Doom", result.Title);        // gamebox identity preserved
    }

    [Fact]
    public void Returns_baseline_unchanged_when_no_match()
    {
        var resolver = new ProfileResolver(Catalog(DoomEntry()));
        var baseline = new GameProfile { Title = "Unknown" };

        Assert.Same(baseline, resolver.Resolve(baseline, ["mystery.exe"]));
    }

    [Fact]
    public void Never_clobbers_a_user_override()
    {
        var resolver = new ProfileResolver(Catalog(DoomEntry()));
        var baseline = new GameProfile
        {
            Title = "My Doom",
            Origin = ProfileOrigin.UserOverride,
            Cpu = new CpuSpec { CyclesMode = CyclesMode.Fixed, FixedCycles = 999 },
        };

        var result = resolver.Resolve(baseline, ["doom.exe", "doom.wad"]);

        Assert.Same(baseline, result);
        Assert.Equal(999, result.Cpu.FixedCycles);
    }

    [Fact]
    public async Task Import_with_resolver_auto_applies_curated_config()
    {
        var paths = new AppPaths(TempRoot());
        var store = new GameboxStore();
        var catalog = new CatalogDatabase(Path.Combine(paths.DataRoot, "catalog.db"));
        catalog.Build([new CatalogEntry
        {
            Id = "doom",
            Title = "DOOM",
            Telltales = ["doom.exe"],
            Profile = new GameProfile
            {
                Title = "DOOM",
                Origin = ProfileOrigin.CuratedBase,
                Cpu = new CpuSpec { CyclesMode = CyclesMode.Fixed, FixedCycles = 20000 },
            },
        }]);
        var pipeline = new ImportPipeline(paths, store, new ProfileResolver(catalog));

        var source = Path.Combine(TempRoot(), "DoomGame");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "DOOM.EXE"), "x");

        var result = await pipeline.ImportAsync(source);

        Assert.True(result.Success, result.Error);
        var profile = store.ReadProfile(result.GameboxPath!);
        Assert.Equal(ProfileOrigin.CuratedBase, profile.Origin);
        Assert.Equal(20000, profile.Cpu.FixedCycles);
    }

    private static CatalogDatabase Catalog(params CatalogEntry[] entries)
    {
        var db = new CatalogDatabase(Path.Combine(TempRoot(), "catalog.db"));
        db.Build(entries);
        return db;
    }

    private static CatalogEntry DoomEntry() => new()
    {
        Id = "doom",
        Title = "DOOM",
        Telltales = ["doom.exe", "doom.wad"],
        Profile = new GameProfile
        {
            Title = "DOOM",
            Origin = ProfileOrigin.CuratedBase,
            Cpu = new CpuSpec { CyclesMode = CyclesMode.Fixed, FixedCycles = 20000 },
        },
    };

    private static string TempRoot() =>
        Path.Combine(Path.GetTempPath(), "emudos-tests", Guid.NewGuid().ToString("N"));
}
