using System;
using System.IO;
using EmuDOS.Core.Import;
using Xunit;

namespace EmuDOS.Tests;

public class AutobootDbpTests
{
    [Fact]
    public void Parses_C_drive_target_when_the_file_exists()
    {
        var dir = NewTempDir();
        Directory.CreateDirectory(Path.Combine(dir, "ID", "T7G"));
        File.WriteAllText(Path.Combine(dir, "ID", "T7G", "T7G.BAT"), "@v !");
        File.WriteAllText(Path.Combine(dir, "AUTOBOOT.DBP"), "C:\\ID\\T7G\\T7G.BAT\r\n10");

        Assert.Equal("ID\\T7G\\T7G.BAT", AutobootDbp.TryParseExecutable(dir));
    }

    [Fact]
    public void Null_when_the_target_does_not_exist()
    {
        var dir = NewTempDir();
        File.WriteAllText(Path.Combine(dir, "AUTOBOOT.DBP"), "C:\\GAME\\MISSING.EXE\r\n10");

        Assert.Null(AutobootDbp.TryParseExecutable(dir)); // stale entry -> ignore
    }

    [Fact]
    public void Null_when_the_target_is_on_the_CD_not_C()
    {
        var dir = NewTempDir();
        File.WriteAllText(Path.Combine(dir, "AUTOBOOT.DBP"), "D:\\GAME.EXE\r\n5");

        Assert.Null(AutobootDbp.TryParseExecutable(dir)); // a raw-CD auto-run, not an install
    }

    [Fact]
    public void Null_when_there_is_no_dbp()
    {
        Assert.Null(AutobootDbp.TryParseExecutable(NewTempDir()));
    }

    private static string NewTempDir()
    {
        var d = Path.Combine(Path.GetTempPath(), "emudos_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        return d;
    }
}
