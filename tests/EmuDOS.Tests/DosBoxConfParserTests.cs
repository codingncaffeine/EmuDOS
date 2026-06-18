using EmuDOS.Core.Catalog;
using EmuDOS.Core.Engine.DosBoxPure;
using EmuDOS.Core.Model;

namespace EmuDOS.Tests;

public class DosBoxConfParserTests
{
    private const string SampleConf = """
        [dosbox]
        machine=vga
        memsize=32

        [cpu]
        core=dynamic
        cputype=pentium_slow
        cycles=fixed 20000

        [sblaster]
        sbtype=sbpro2
        sbbase=240
        irq=5
        dma=1
        hdma=5
        oplmode=opl3

        [gus]
        gus=false

        [midi]
        mididevice=mt32

        [dos]
        xms=true
        ems=false
        umb=true

        [joystick]
        joysticktype=ch

        [autoexec]
        mount c .
        c:
        GAME.EXE
        exit
        """;

    [Fact]
    public void Parses_a_full_conf_into_a_curated_profile()
    {
        var p = DosBoxConfParser.Parse(SampleConf, "Sample");

        Assert.Equal("Sample", p.Title);
        Assert.Equal(ProfileOrigin.CuratedBase, p.Origin);

        Assert.Equal(MachineType.Vga, p.Machine.Machine);
        Assert.Equal(32, p.Memory.SizeMb);
        Assert.True(p.Memory.Xms);
        Assert.False(p.Memory.Ems);

        Assert.Equal(CpuCore.Dynamic, p.Cpu.Core);
        Assert.Equal(CpuType.PentiumSlow, p.Cpu.Type);
        Assert.Equal(CyclesMode.Fixed, p.Cpu.CyclesMode);
        Assert.Equal(20000, p.Cpu.FixedCycles);

        Assert.Equal(SoundBlasterType.SbPro2, p.Sound.SoundBlaster);
        Assert.Equal(0x240, p.Sound.Port);
        Assert.Equal(5, p.Sound.Irq);
        Assert.Equal(AdlibMode.Opl3, p.Sound.Adlib);
        Assert.Equal(MidiDevice.Mt32, p.Sound.Midi);

        Assert.Equal(JoystickType.Ch, p.Joystick.Type);

        // The autoexec already launches the game, so Executable stays empty.
        Assert.Null(p.Launch.Executable);
        Assert.Contains("mount c .", p.Launch.PreCommands);
        Assert.Contains("GAME.EXE", p.Launch.PreCommands);
    }

    [Theory]
    [InlineData("max", CyclesMode.Max, 0)]
    [InlineData("auto", CyclesMode.Auto, 0)]
    [InlineData("fixed 5000", CyclesMode.Fixed, 5000)]
    [InlineData("8000", CyclesMode.Fixed, 8000)]
    public void Parses_cycles_variants(string cycles, CyclesMode mode, int value)
    {
        var p = DosBoxConfParser.Parse($"[cpu]\ncycles={cycles}\n");
        Assert.Equal(mode, p.Cpu.CyclesMode);
        Assert.Equal(value, p.Cpu.FixedCycles);
    }

    [Fact]
    public void Empty_conf_yields_sensible_defaults()
    {
        var p = DosBoxConfParser.Parse("");

        Assert.Equal(MachineType.Svga, p.Machine.Machine);
        Assert.Equal(SoundBlasterType.Sb16, p.Sound.SoundBlaster);
        Assert.Equal(CyclesMode.Auto, p.Cpu.CyclesMode);
    }

    [Fact]
    public void Parsed_profile_drives_the_adapter_end_to_end()
    {
        var plan = DosBoxPureAdapter.BuildLaunchPlan(DosBoxConfParser.Parse(SampleConf, "Sample"));

        Assert.Equal("vga", plan.CoreOptions["dosbox_pure_machine"]);
        Assert.Equal("sbpro2", plan.CoreOptions["dosbox_pure_sblaster_type"]);
        Assert.Equal("pentium_slow", plan.CoreOptions["dosbox_pure_cpu_type"]);

        // Exact (non-preset) cycles and the joystick type land in the generated DOSBOX.BAT.
        Assert.Contains("CYCLES fixed 20000", plan.AutoexecBat);
        Assert.Contains("joysticktype=ch", plan.AutoexecBat);
        Assert.Contains("GAME.EXE", plan.AutoexecBat);
    }
}
