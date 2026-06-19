using System.IO.Compression;
using EmuDOS.Core.Import;
using EmuDOS.Core.Infrastructure;
using EmuDOS.Core.Library;

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
        Assert.True(File.Exists(Path.Combine(result.GameboxPath!, "content", "Some Game.iso")));

        var launch = store.ReadProfile(result.GameboxPath!).Launch;
        Assert.Null(launch.Executable);
        Assert.Contains(launch.PreCommands, c => c.Contains("IMGMOUNT D:") && c.Contains("Some Game.iso"));
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
