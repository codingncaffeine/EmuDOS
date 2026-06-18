using EmuDOS.Core.Catalog;

namespace EmuDOS.Tests;

public class CatalogSeederTests
{
    [Fact]
    public void Seeds_catalog_from_an_exodos_style_tree()
    {
        var dosRoot = TempDir();
        MakeGame(dosRoot, "Doom", cycles: 20000, exe: "DOOM.EXE");
        MakeGame(dosRoot, "Keen", cycles: 8000, exe: "KEEN.EXE");
        var catalog = NewCatalog();

        var count = new CatalogSeeder().SeedFromExoDos(dosRoot, catalog);

        Assert.Equal(2, count);
        var doom = catalog.Match(["DOOM.EXE"]);
        Assert.NotNull(doom);
        Assert.Equal("Doom", doom!.Title);
        Assert.Equal(20000, doom.Cpu.FixedCycles);
        Assert.Equal(8000, catalog.Match(["KEEN.EXE"])!.Cpu.FixedCycles);
    }

    [Fact]
    public void Skips_folders_without_a_conf()
    {
        var dosRoot = TempDir();
        Directory.CreateDirectory(Path.Combine(dosRoot, "NotAGame"));
        MakeGame(dosRoot, "Real", cycles: 1000, exe: "REAL.EXE");

        Assert.Equal(1, new CatalogSeeder().SeedFromExoDos(dosRoot, NewCatalog()));
    }

    [Fact]
    public void Installer_executables_are_excluded_from_telltales()
    {
        var dosRoot = TempDir();
        var dir = Path.Combine(dosRoot, "Game");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "dosbox.conf"), "[cpu]\ncycles=auto\n");
        File.WriteAllText(Path.Combine(dir, "GAME.EXE"), "x");
        File.WriteAllText(Path.Combine(dir, "INSTALL.EXE"), "x");
        var catalog = NewCatalog();

        new CatalogSeeder().SeedFromExoDos(dosRoot, catalog);

        // Telltale is GAME.EXE only, so a copy without the installer still matches.
        Assert.NotNull(catalog.Match(["GAME.EXE"]));
    }

    private static void MakeGame(string dosRoot, string name, int cycles, string exe)
    {
        var dir = Path.Combine(dosRoot, name);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "dosbox.conf"),
            $"[cpu]\ncycles=fixed {cycles}\n[autoexec]\n{exe}\n");
        File.WriteAllText(Path.Combine(dir, exe), "x");
    }

    private static CatalogDatabase NewCatalog() =>
        new(Path.Combine(TempDir(), "catalog.db"));

    private static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "emudos-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
