using EmuDOS.Core.Engine.DosBoxPure;
using EmuDOS.Core.Model;

namespace EmuDOS.Tests;

public class DosBoxPureAdapterTests
{
    [Fact]
    public void Default_profile_maps_to_generic_dosbox_pure_options()
    {
        var options = DosBoxPureAdapter.BuildCoreOptions(new GameProfile());

        Assert.Equal("svga", options["dosbox_pure_machine"]);
        Assert.Equal("auto", options["dosbox_pure_cpu_type"]);
        Assert.Equal("auto", options["dosbox_pure_cycles"]);
        Assert.Equal("16", options["dosbox_pure_memory_size"]);
        Assert.Equal("sb16", options["dosbox_pure_sblaster_type"]);
        Assert.Equal("A220 I7 D1 H5", options["dosbox_pure_sblaster_conf"]);
        Assert.Equal("48000", options["dosbox_pure_audiorate"]);
        Assert.Equal("disabled", options["dosbox_pure_midi"]);
    }

    [Fact]
    public void Sb_resources_snap_to_exact_preset_when_they_match()
    {
        Assert.Equal("A220 I7 D1 H5", DosBoxPureAdapter.SnapSoundBlasterConf(0x220, 7, 1, 5));
        Assert.Equal("A240 I10 D3 H7", DosBoxPureAdapter.SnapSoundBlasterConf(0x240, 10, 3, 7));
        Assert.Equal("A280 I5 D1 H5", DosBoxPureAdapter.SnapSoundBlasterConf(0x280, 5, 1, 5));
    }

    [Fact]
    public void Sb_resources_snap_to_nearest_when_no_exact_preset()
    {
        // Port 0x240, IRQ 7 matches two presets exactly on port+irq; DMA tie-break picks D1 H5.
        Assert.Equal("A240 I7 D1 H5", DosBoxPureAdapter.SnapSoundBlasterConf(0x240, 7, 1, 6));
        // Unknown port 0x300 → port penalty equal for all; closest IRQ wins (7).
        Assert.Equal("A220 I7 D1 H5", DosBoxPureAdapter.SnapSoundBlasterConf(0x300, 7, 1, 5));
    }

    [Fact]
    public void Fixed_cycles_set_nearest_preset_option_and_exact_value_in_bat()
    {
        var profile = new GameProfile
        {
            Cpu = new CpuSpec { CyclesMode = CyclesMode.Fixed, FixedCycles = 12000 },
        };

        var plan = DosBoxPureAdapter.BuildLaunchPlan(profile);

        // 12000 is nearest the 13400 preset.
        Assert.Equal("13400", plan.CoreOptions["dosbox_pure_cycles"]);
        // …but the exact value is pinned from autoexec.
        Assert.Contains("CYCLES fixed 12000", plan.AutoexecBat);
    }

    [Fact]
    public void Memory_flags_do_not_emit_live_config_overrides_before_launch()
    {
        // ems/xms/umb are live (WhenIdle) DOS settings — applying them in the autoexec faults the
        // game launched immediately after, so we must not emit them.
        var profile = new GameProfile
        {
            Memory = new MemorySpec { Ems = false, Xms = false, Umb = false },
            Launch = new LaunchSpec { Executable = "GAME.EXE" },
        };

        var bat = DosBoxPureAdapter.BuildAutoexecBat(profile);

        Assert.DoesNotContain("dos ems=false", bat);
        Assert.DoesNotContain("dos xms=false", bat);
        Assert.DoesNotContain("dos umb=false", bat);
        Assert.Contains("@GAME.EXE", bat);
    }

    [Fact]
    public void Precommands_and_executable_render_in_order()
    {
        var profile = new GameProfile
        {
            Launch = new LaunchSpec
            {
                PreCommands = ["MOUNT D . -t cdrom", "SET BLASTER=A220 I7 D1 T5"],
                Executable = "GAME.EXE",
                Arguments = "-nosound",
            },
        };

        var bat = DosBoxPureAdapter.BuildAutoexecBat(profile);
        var lines = bat.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        Assert.Contains("MOUNT D . -t cdrom", lines);
        Assert.Contains("SET BLASTER=A220 I7 D1 T5", lines);
        Assert.Equal("@GAME.EXE -nosound", lines[^1]);
    }

    [Fact]
    public void Executable_in_a_subfolder_is_launched_from_that_folder()
    {
        var profile = new GameProfile { Launch = new LaunchSpec { Executable = @"ABUSE\ABUSE.EXE" } };

        var bat = DosBoxPureAdapter.BuildAutoexecBat(profile);
        var lines = bat.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        Assert.Contains(@"@CD \ABUSE", lines);
        Assert.Equal("@ABUSE.EXE", lines[^1]);
    }

    [Fact]
    public void Root_executable_runs_without_a_cd()
    {
        var bat = DosBoxPureAdapter.BuildAutoexecBat(
            new GameProfile { Launch = new LaunchSpec { Executable = "INSTALL.EXE" } });

        Assert.DoesNotContain("@CD ", bat);
        Assert.Contains("@INSTALL.EXE", bat);
    }

    [Fact]
    public void Joystick_type_emits_config_line_when_not_auto()
    {
        var ch = DosBoxPureAdapter.BuildAutoexecBat(
            new GameProfile { Joystick = new JoystickSpec { Type = JoystickType.Ch } });
        Assert.Contains("joystick joysticktype=ch", ch);

        var auto = DosBoxPureAdapter.BuildAutoexecBat(new GameProfile());
        Assert.DoesNotContain("joysticktype", auto);
    }

    [Fact]
    public void Vga_machine_omits_svga_only_options()
    {
        var options = DosBoxPureAdapter.BuildCoreOptions(
            new GameProfile { Machine = new MachineSpec { Machine = MachineType.Vga } });

        Assert.Equal("vga", options["dosbox_pure_machine"]);
        Assert.False(options.ContainsKey("dosbox_pure_svga"));
    }

    [Fact]
    public void Midi_soundfont_passes_through_when_device_selected()
    {
        var options = DosBoxPureAdapter.BuildCoreOptions(new GameProfile
        {
            Sound = new SoundSpec { Midi = MidiDevice.GeneralMidi, MidiSoundFont = "GM.sf2" },
        });

        Assert.Equal("GM.sf2", options["dosbox_pure_midi"]);
    }

    [Fact]
    public void Iso_forces_the_start_menu_until_the_game_is_installed()
    {
        var iso = new GameProfile { SourceMedia = SourceMediaType.Iso };

        // Fresh CD (no AUTOBOOT.DBP yet): keep the menu open for install / boot-OS.
        var fresh = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "emudos_iso_" + System.Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(fresh);
        Assert.Equal("-1", DosBoxPureAdapter.BuildCoreOptions(iso, fresh)["dosbox_pure_menu_time"]);

        // Installed CD game (AUTOBOOT.DBP present): don't force the menu — let it auto-start.
        System.IO.File.WriteAllText(System.IO.Path.Combine(fresh, "AUTOBOOT.DBP"), "C:\\GAME\\RUN.BAT");
        Assert.False(DosBoxPureAdapter.BuildCoreOptions(iso, fresh).ContainsKey("dosbox_pure_menu_time"));
    }
}
