using EmuDOS.Core.Import;
using Xunit;

namespace EmuDOS.Tests;

public class DosBoxBatLaunchTests
{
    [Fact]
    public void Reproduces_the_eXoDOS_recipe_when_it_mounts_a_disc()
    {
        var dir = NewTempDir();
        File.WriteAllText(Path.Combine(dir, "DOSBOX.BAT"),
            "@ECHO OFF\r\nIMGMOUNT D: \"c:\\gamecd.cue\" -t cdrom\r\n@CD \\SystemSh\\SSHOCK\r\n@SHOCKGUS.BAT\r\n");

        var launch = DosBoxBatLaunch.TryParse(dir);

        Assert.NotNull(launch);
        Assert.Null(launch!.Executable); // the run is part of the recipe, not a separate exe pick
        Assert.Equal(
            new[] { "IMGMOUNT D: \"c:\\gamecd.cue\" -t cdrom", "CD \\SystemSh\\SSHOCK", "SHOCKGUS.BAT" },
            launch.PreCommands);
    }

    [Fact]
    public void Takes_over_when_the_bat_runs_a_program()
    {
        var dir = NewTempDir();
        // The heuristic can mis-pick a config tool here; the bat names the real game launcher.
        File.WriteAllText(Path.Combine(dir, "DOSBOX.BAT"), "@ECHO OFF\r\n@CD \\pqswat\r\n@PQSWATForDOSBox.exe\r\n");

        var launch = DosBoxBatLaunch.TryParse(dir);

        Assert.NotNull(launch);
        Assert.Null(launch!.Executable);
        Assert.Equal(new[] { "CD \\pqswat", "PQSWATForDOSBox.exe" }, launch.PreCommands);
    }

    [Fact]
    public void Leaves_a_do_nothing_bat_to_the_normal_exe_pick()
    {
        var dir = NewTempDir();
        File.WriteAllText(Path.Combine(dir, "DOSBOX.BAT"), "@ECHO OFF\r\n@CYCLES fixed 45000\r\n");

        Assert.Null(DosBoxBatLaunch.TryParse(dir)); // no mount, nothing to run -> leave the exe pick
    }

    [Fact]
    public void Null_when_there_is_no_bat()
    {
        Assert.Null(DosBoxBatLaunch.TryParse(NewTempDir()));
    }

    private static string NewTempDir()
    {
        var d = Path.Combine(Path.GetTempPath(), "emudos_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        return d;
    }
}
