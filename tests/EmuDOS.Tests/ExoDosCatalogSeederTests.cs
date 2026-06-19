using EmuDOS.Core.Catalog;
using EmuDOS.Core.Model;

namespace EmuDOS.Tests;

public class ExoDosCatalogSeederTests
{
    [Fact]
    public void Parses_an_exodos_game_folder_into_a_catalog_entry()
    {
        var root = TempDir();
        var game = Path.Combine(root, "007Licen");
        Directory.CreateDirectory(game);
        File.WriteAllText(Path.Combine(game, "007 - Licence to Kill (1989).bat"), "@echo off");
        File.WriteAllText(Path.Combine(game, "install.bat"), "");
        File.WriteAllText(Path.Combine(game, "dosbox.conf"),
            "[cpu]\ncycles=1400\ncore=auto\n[dosbox]\nmachine=svga_s3\nmemsize=8\n"
            + "[autoexec]\nmount c .\\eXoDOS\\\nc:\ncd 007Licen\ncls\n@BONDE\nexit\n");

        var entry = Assert.Single(new ExoDosCatalogSeeder().BuildEntries(root).ToList());

        Assert.Equal("007 - Licence to Kill", entry.Title);       // title from .bat, year stripped
        Assert.Equal("bonde", Assert.Single(entry.Telltales));    // exe from autoexec, basename
        Assert.Equal(MachineType.Svga, entry.Profile.Machine.Machine);
        Assert.Equal(CyclesMode.Fixed, entry.Profile.Cpu.CyclesMode);
        Assert.Equal(1400, entry.Profile.Cpu.FixedCycles);
        Assert.Empty(entry.Profile.Launch.PreCommands);           // eXoDOS-specific autoexec dropped
        Assert.Equal(ProfileOrigin.CuratedBase, entry.Profile.Origin);
    }

    [Fact]
    public void Includes_run_bat_games_with_no_visible_exe_matched_by_title()
    {
        // The real eXoDOS pattern: launch via "call run", so no exe is visible in the metadata.
        var root = TempDir();
        var game = Path.Combine(root, "DOOM");
        Directory.CreateDirectory(game);
        File.WriteAllText(Path.Combine(game, "DOOM (1993).bat"), "@echo off");
        File.WriteAllText(Path.Combine(game, "dosbox.conf"),
            "[cpu]\ncycles=auto\n[autoexec]\ncls\nmount c .\\eXoDOS\\DOOM\nc:\n@cls\ncall run\n");

        var entry = Assert.Single(new ExoDosCatalogSeeder().BuildEntries(root).ToList());

        Assert.Equal("DOOM", entry.Title);
        Assert.Empty(entry.Telltales); // matched by title, not content
    }

    private static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "emudos-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
