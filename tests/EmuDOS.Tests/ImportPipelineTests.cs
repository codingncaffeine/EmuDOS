using System.IO.Compression;
using EmuDOS.Core.Import;
using EmuDOS.Core.Infrastructure;
using EmuDOS.Core.Library;
using EmuDOS.Core.Model;

namespace EmuDOS.Tests;

public class ImportPipelineTests
{
    [Fact]
    public async Task Imports_a_folder_with_a_game_exe_as_ready_to_play()
    {
        var source = TempDir();
        File.WriteAllText(Path.Combine(source, "DOOM.EXE"), "x");
        File.WriteAllText(Path.Combine(source, "readme.txt"), "x");
        var (pipeline, store) = NewPipeline();

        var result = await pipeline.ImportAsync(source);

        Assert.True(result.Success, result.Error);
        Assert.Equal(ImportClassification.ReadyToPlay, result.Classification);
        Assert.Equal("DOOM.EXE", result.ChosenExecutable);
        Assert.True(store.IsGamebox(result.GameboxPath!));
        Assert.True(File.Exists(Path.Combine(result.GameboxPath!, "content", "DOOM.EXE")));
        Assert.Equal("DOOM.EXE", store.ReadProfile(result.GameboxPath!).Launch.Executable);
    }

    [Fact]
    public async Task Preinstalled_game_with_autoboot_beside_its_cd_is_ready_not_a_raw_disc()
    {
        // Mirrors the real 7th Guest shape: game installed under ID\T7G, AUTOBOOT.DBP naming it, the
        // CD nested in spaced folders, and Mac-zip junk — all of which previously made it import as Iso.
        var source = TempDir();
        var t7g = Path.Combine(source, "ID", "T7G");
        Directory.CreateDirectory(t7g);
        File.WriteAllText(Path.Combine(t7g, "T7G.BAT"), "@v !");
        File.WriteAllText(Path.Combine(t7g, "V.EXE"), "x");
        File.WriteAllText(Path.Combine(t7g, "INSTALL.EXE"), "x");
        File.WriteAllText(Path.Combine(source, "AUTOBOOT.DBP"), "C:\\ID\\T7G\\T7G.BAT\r\n10");
        Directory.CreateDirectory(Path.Combine(source, "__MACOSX"));
        File.WriteAllText(Path.Combine(source, "__MACOSX", "._AUTOBOOT.DBP"), "junk");
        var disc = Path.Combine(source, "My Game (CD-ROM)", "Disc 1");
        Directory.CreateDirectory(disc);
        File.WriteAllText(Path.Combine(disc, "G_DISC1.bin"), "1");
        File.WriteAllText(Path.Combine(disc, "G_DISC1.cue"), "FILE \"G_DISC1.bin\" BINARY\n  TRACK 01 MODE1/2352\n");
        var (pipeline, store) = NewPipeline();

        var result = await pipeline.ImportAsync(source);

        Assert.True(result.Success, result.Error);
        var profile = store.ReadProfile(result.GameboxPath!);
        Assert.NotEqual(SourceMediaType.Iso, profile.SourceMedia); // mounted as C:, not a raw disc
        Assert.Equal("ID\\T7G\\T7G.BAT", profile.Launch.Executable); // the AUTOBOOT target, not INSTALL.EXE
        Assert.Equal(ImportClassification.ReadyToPlay, result.Classification);
    }

    [Fact]
    public async Task Bundles_a_multi_disc_set_onto_one_swappable_drive()
    {
        var source = TempDir();
        File.WriteAllText(Path.Combine(source, "GAME.EXE"), "x");
        // Two discs nested in spaced folders — the messy real-world shape that can't IMGMOUNT in place.
        var d1 = Path.Combine(source, "My Game (CD-ROM)", "Disc 1");
        var d2 = Path.Combine(source, "My Game (CD-ROM)", "Disc 2");
        Directory.CreateDirectory(d1);
        Directory.CreateDirectory(d2);
        File.WriteAllText(Path.Combine(d1, "GAME_DISC1.bin"), "1");
        File.WriteAllText(Path.Combine(d1, "GAME_DISC1.cue"), "FILE \"GAME_DISC1.bin\" BINARY\n  TRACK 01 MODE1/2352\n");
        File.WriteAllText(Path.Combine(d2, "GAME_DISC2.bin"), "2");
        File.WriteAllText(Path.Combine(d2, "GAME_DISC2.cue"), "FILE \"GAME_DISC2.bin\" BINARY\n  TRACK 01 MODE1/2352\n");
        var (pipeline, store) = NewPipeline();

        var result = await pipeline.ImportAsync(source);

        Assert.True(result.Success, result.Error);
        var content = Path.Combine(result.GameboxPath!, "content");
        // Both discs flattened to the root and mounted on a single D: so dosbox_pure can swap them.
        Assert.True(File.Exists(Path.Combine(content, "gamecd01.cue")));
        Assert.True(File.Exists(Path.Combine(content, "gamecd02.cue")));
        var mount = Assert.Single(store.ReadProfile(result.GameboxPath!).Launch.PreCommands,
            c => c.Contains("IMGMOUNT D:", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("gamecd01.cue", mount);
        Assert.Contains("gamecd02.cue", mount);
    }

    [Fact]
    public async Task Installer_only_folder_is_needs_install()
    {
        var source = TempDir();
        File.WriteAllText(Path.Combine(source, "INSTALL.EXE"), "x");
        var (pipeline, _) = NewPipeline();

        var result = await pipeline.ImportAsync(source);

        Assert.Equal(ImportClassification.NeedsInstall, result.Classification);
        Assert.Equal("INSTALL.EXE", result.ChosenExecutable);
    }

    [Fact]
    public async Task Title_matching_executable_is_preferred()
    {
        var source = Path.Combine(Path.GetTempPath(), "emudos-tests", Guid.NewGuid().ToString("N"), "KEEN");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "INTRO.EXE"), "x");
        File.WriteAllText(Path.Combine(source, "KEEN.EXE"), "x");
        var (pipeline, _) = NewPipeline();

        var result = await pipeline.ImportAsync(source);

        Assert.Equal(ImportClassification.ReadyToPlay, result.Classification);
        Assert.Equal("KEEN.EXE", result.ChosenExecutable);
    }

    [Fact]
    public async Task Dos_extender_is_skipped_for_the_bat_launcher()
    {
        // Mirrors Grand Theft Auto: the import must not pick the DOS/4GW extender as the program.
        var source = TempDir();
        Directory.CreateDirectory(Path.Combine(source, "GTADOS"));
        File.WriteAllText(Path.Combine(source, "GTADOS", "DOS4GW.EXE"), "x");
        File.WriteAllText(Path.Combine(source, "GTADOS", "GTA.EXE"), "x");
        File.WriteAllText(Path.Combine(source, "GTADOS.BAT"), "x");
        var (pipeline, _) = NewPipeline();

        var result = await pipeline.ImportAsync(source);

        Assert.Equal(ImportClassification.ReadyToPlay, result.Classification);
        Assert.Equal("GTADOS.BAT", result.ChosenExecutable);
    }

    [Fact]
    public async Task Imports_a_zip_with_a_nested_executable()
    {
        var dir = TempDir();
        var zip = Path.Combine(dir, "game.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            using var s = archive.CreateEntry("GAME/RUN.EXE").Open();
            s.WriteByte(1);
        }
        var (pipeline, store) = NewPipeline();

        var result = await pipeline.ImportAsync(zip);

        Assert.True(result.Success, result.Error);
        Assert.Equal(ImportClassification.ReadyToPlay, result.Classification);
        Assert.Equal("GAME\\RUN.EXE", result.ChosenExecutable);
        Assert.True(File.Exists(Path.Combine(result.GameboxPath!, "content", "GAME", "RUN.EXE")));
    }

    [Fact]
    public async Task Disc_image_is_imported_as_a_mounted_cd_needing_install()
    {
        var dir = TempDir();
        var iso = Path.Combine(dir, "Some Game.iso");
        File.WriteAllBytes(iso, new byte[4096]);
        var (pipeline, store) = NewPipeline();

        var result = await pipeline.ImportAsync(iso);

        Assert.True(result.Success, result.Error);
        Assert.Equal(ImportClassification.NeedsInstall, result.Classification);
        // Copied under a short, space-free name so DOS/IMGMOUNT can open it.
        Assert.True(File.Exists(Path.Combine(result.GameboxPath!, "content", "disc.iso")));

        var launch = store.ReadProfile(result.GameboxPath!).Launch;
        Assert.Null(launch.Executable);
        Assert.Contains(launch.PreCommands, c => c.Contains("IMGMOUNT D:") && c.Contains("disc.iso"));
    }

    [Fact]
    public void Strips_disc_markers_from_titles()
    {
        Assert.Equal("Quest for Glory", ImportPipeline.StripDiscMarker("Quest for Glory (Disc 1)"));
        Assert.Equal("Quest for Glory", ImportPipeline.StripDiscMarker("Quest for Glory CD2"));
        Assert.Equal("Quest for Glory", ImportPipeline.StripDiscMarker("Quest for Glory - Disk 3"));
        Assert.Equal("Doom", ImportPipeline.StripDiscMarker("Doom"));
    }

    [Fact]
    public void Groups_disc_images_of_one_game_into_a_set()
    {
        string[] paths =
        [
            @"C:\g\Quest (Disc 1).iso",
            @"C:\g\Quest (Disc 2).iso",
            @"C:\g\Other Game.iso",
        ];

        var sets = ImportPipeline.GroupDiscSets(paths).ToList();

        Assert.Equal(2, sets.Count);
        Assert.Contains(sets, s => s.Count == 2); // Quest's two discs together
        Assert.Contains(sets, s => s.Count == 1); // the unrelated game alone
    }

    [Fact]
    public async Task Multi_disc_bundle_imports_as_one_gamebox_with_all_discs()
    {
        var dir = TempDir();
        var cd1 = Path.Combine(dir, "Big Game (Disc 1).iso");
        var cd2 = Path.Combine(dir, "Big Game (Disc 2).iso");
        File.WriteAllBytes(cd1, new byte[4096]);
        File.WriteAllBytes(cd2, new byte[4096]);
        var (pipeline, store) = NewPipeline();

        var result = await pipeline.ImportDiscSetAsync([cd1, cd2]);

        Assert.True(result.Success, result.Error);
        Assert.Equal(ImportClassification.NeedsInstall, result.Classification);
        var content = Path.Combine(result.GameboxPath!, "content");
        Assert.True(File.Exists(Path.Combine(content, "Big Game (Disc 1).iso")));
        Assert.True(File.Exists(Path.Combine(content, "Big Game (Disc 2).iso")));
        Assert.Equal("Big Game", store.ReadProfile(result.GameboxPath!).Title);
    }

    private static (ImportPipeline, GameboxStore) NewPipeline()
    {
        var paths = new AppPaths(Path.Combine(Path.GetTempPath(), "emudos-tests", Guid.NewGuid().ToString("N")));
        var store = new GameboxStore();
        return (new ImportPipeline(paths, store), store);
    }

    private static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "emudos-tests", Guid.NewGuid().ToString("N"), "src");
        Directory.CreateDirectory(dir);
        return dir;
    }
}
