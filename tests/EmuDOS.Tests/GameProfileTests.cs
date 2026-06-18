using EmuDOS.Core.Model;

namespace EmuDOS.Tests;

public class GameProfileTests
{
    [Fact]
    public void Default_profile_has_sensible_generic_dos_defaults()
    {
        var profile = new GameProfile();

        Assert.Equal(1, profile.SchemaVersion);
        Assert.Equal(ProfileOrigin.Default, profile.Origin);
        Assert.Equal(SourceMediaType.Folder, profile.SourceMedia);

        Assert.Equal(CpuType.Auto, profile.Cpu.Type);
        Assert.Equal(CyclesMode.Auto, profile.Cpu.CyclesMode);

        Assert.Equal(MachineType.Svga, profile.Machine.Machine);

        Assert.Equal(16, profile.Memory.SizeMb);
        Assert.True(profile.Memory.Xms);
        Assert.True(profile.Memory.Ems);

        Assert.Equal(SoundBlasterType.Sb16, profile.Sound.SoundBlaster);
        Assert.Equal(0x220, profile.Sound.Port);
        Assert.Equal(7, profile.Sound.Irq);
        Assert.Equal(MidiDevice.None, profile.Sound.Midi);

        Assert.Empty(profile.Mounts);
        Assert.Empty(profile.Launch.PreCommands);
    }

    [Fact]
    public void Records_use_value_equality()
    {
        var a = new GameProfile { Title = "Game", Cpu = new CpuSpec { CyclesMode = CyclesMode.Max } };
        var b = new GameProfile { Title = "Game", Cpu = new CpuSpec { CyclesMode = CyclesMode.Max } };

        Assert.Equal(a, b);
    }

    [Fact]
    public void With_expression_supports_override_layering_without_mutating_base()
    {
        var curated = new GameProfile
        {
            Title = "Game",
            Origin = ProfileOrigin.CuratedBase,
            Cpu = new CpuSpec { CyclesMode = CyclesMode.Fixed, FixedCycles = 12000 },
        };

        // A user override raises the cycle count; the curated base must be untouched.
        var effective = curated with
        {
            Origin = ProfileOrigin.UserOverride,
            Cpu = curated.Cpu with { FixedCycles = 20000 },
        };

        Assert.Equal(12000, curated.Cpu.FixedCycles);
        Assert.Equal(20000, effective.Cpu.FixedCycles);
        Assert.Equal(ProfileOrigin.CuratedBase, curated.Origin);
        Assert.Equal(ProfileOrigin.UserOverride, effective.Origin);
    }
}
