using EmuDOS.Core.Catalog;
using EmuDOS.Core.Model;

namespace EmuDOS.Tests;

public class CatalogDatabaseTests
{
    [Fact]
    public void Matches_and_returns_curated_profile_when_all_telltales_present()
    {
        var db = NewCatalog();
        db.Build([Entry("doom", "Doom", ["doom.exe", "doom.wad"], 20000), Entry("keen", "Keen", ["keen.exe"], 8000)]);

        var match = db.Match(["DOOM.EXE", "doom.wad", "setup.exe"]);

        Assert.NotNull(match);
        Assert.Equal("Doom", match!.Title);
        Assert.Equal(20000, match.Cpu.FixedCycles);
    }

    [Fact]
    public void Requires_every_telltale_to_be_present()
    {
        var db = NewCatalog();
        db.Build([Entry("doom", "Doom", ["doom.exe", "doomdata.dat"], 20000)]);

        Assert.Null(db.Match(["doom.exe"])); // doomdata missing
    }

    [Fact]
    public void Most_specific_entry_wins()
    {
        var db = NewCatalog();
        db.Build([Entry("generic", "Generic", ["game.exe"], 5000), Entry("special", "Special", ["game.exe", "data.dat"], 12000)]);

        var match = db.Match(["game.exe", "data.dat"]);

        Assert.Equal("Special", match!.Title);
    }

    [Fact]
    public void Unknown_content_returns_null()
    {
        var db = NewCatalog();
        db.Build([Entry("doom", "Doom", ["doom.exe"], 1)]);

        Assert.Null(db.Match(["mystery.exe"]));
    }

    [Fact]
    public void Build_replaces_existing_catalog()
    {
        var db = NewCatalog();
        db.Build([Entry("a", "A", ["a.exe"], 1), Entry("b", "B", ["b.exe"], 1)]);
        Assert.Equal(2, db.Count);

        db.Build([Entry("c", "C", ["c.exe"], 1)]);

        Assert.Equal(1, db.Count);
        Assert.Null(db.Match(["a.exe"]));
        Assert.NotNull(db.Match(["c.exe"]));
    }

    [Fact]
    public void Matches_by_normalized_title_when_telltales_are_absent()
    {
        var db = NewCatalog();
        db.Build([Entry("doom", "DOOM", [], 20000)]);

        Assert.NotNull(db.MatchByTitle("Doom"));
        Assert.NotNull(db.MatchByTitle("doom (1993)"));
        Assert.Null(db.MatchByTitle("Heretic"));
    }

    [Theory]
    [InlineData("DOOM II", "Doom 2")]
    [InlineData("Wing Commander II (1991)", "wing commander 2")]
    [InlineData("The Secret of Monkey Island", "Secret of Monkey Island")]
    public void Title_normalization_collapses_variants(string a, string b) =>
        Assert.Equal(CatalogDatabase.NormalizeTitle(a), CatalogDatabase.NormalizeTitle(b));

    private static CatalogDatabase NewCatalog() =>
        new(Path.Combine(Path.GetTempPath(), "emudos-tests", Guid.NewGuid().ToString("N"), "catalog.db"));

    private static CatalogEntry Entry(string id, string title, string[] telltales, int cycles) => new()
    {
        Id = id,
        Title = title,
        Telltales = telltales,
        Profile = new GameProfile
        {
            Title = title,
            Origin = ProfileOrigin.CuratedBase,
            Cpu = new CpuSpec { CyclesMode = CyclesMode.Fixed, FixedCycles = cycles },
        },
    };
}
