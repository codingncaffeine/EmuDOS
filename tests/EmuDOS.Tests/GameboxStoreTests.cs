using EmuDOS.Core.Library;
using EmuDOS.Core.Model;

namespace EmuDOS.Tests;

public class GameboxStoreTests
{
    [Fact]
    public void Profile_round_trips_through_json()
    {
        var store = new GameboxStore();
        var box = TempBox();
        var profile = new GameProfile
        {
            Title = "Test Game",
            CanonicalId = "test-1",
            Cpu = new CpuSpec { CyclesMode = CyclesMode.Fixed, FixedCycles = 8000, Type = CpuType.I486Slow },
            Machine = new MachineSpec { Machine = MachineType.Vga },
            Sound = new SoundSpec { SoundBlaster = SoundBlasterType.SbPro2, Irq = 5, Midi = MidiDevice.Mt32 },
            Joystick = new JoystickSpec { Type = JoystickType.Ch },
            Launch = new LaunchSpec { Executable = "GAME.EXE", PreCommands = ["SET X=1"] },
            Mounts = [new MountSpec { DriveLetter = 'D', Kind = MountKind.Cd, Path = "disc.cue", Label = "GAME" }],
            Origin = ProfileOrigin.CuratedBase,
        };

        store.WriteProfile(box, profile);
        var read = store.ReadProfile(box);

        Assert.Equal("Test Game", read.Title);
        Assert.Equal(CyclesMode.Fixed, read.Cpu.CyclesMode);
        Assert.Equal(8000, read.Cpu.FixedCycles);
        Assert.Equal(CpuType.I486Slow, read.Cpu.Type);
        Assert.Equal(MachineType.Vga, read.Machine.Machine);
        Assert.Equal(SoundBlasterType.SbPro2, read.Sound.SoundBlaster);
        Assert.Equal(MidiDevice.Mt32, read.Sound.Midi);
        Assert.Equal(JoystickType.Ch, read.Joystick.Type);
        Assert.Equal("GAME.EXE", read.Launch.Executable);
        Assert.Equal("SET X=1", Assert.Single(read.Launch.PreCommands));
        var mount = Assert.Single(read.Mounts);
        Assert.Equal('D', mount.DriveLetter);
        Assert.Equal(MountKind.Cd, mount.Kind);
        Assert.Equal(ProfileOrigin.CuratedBase, read.Origin);
    }

    [Fact]
    public void Profile_json_is_human_readable_with_string_enums()
    {
        var store = new GameboxStore();
        var box = TempBox();

        store.WriteProfile(box, new GameProfile { Machine = new MachineSpec { Machine = MachineType.Vga } });
        var json = File.ReadAllText(Path.Combine(box, "profile.json"));

        Assert.Contains("\"Vga\"", json);    // enum as name, not number
        Assert.Contains("\n", json);          // indented / multi-line
    }

    [Fact]
    public void Resolve_builds_instance_and_creates_content_and_saves()
    {
        var store = new GameboxStore();
        var box = TempBox();
        store.WriteProfile(box, new GameProfile { Title = "G" });

        var instance = store.Resolve(box);

        Assert.Equal(box, instance.GameboxPath);
        Assert.Equal(Path.Combine(box, "content"), instance.ContentPath);
        Assert.Equal(Path.Combine(box, "saves"), instance.SavePath);
        Assert.True(Directory.Exists(instance.ContentPath));
        Assert.True(Directory.Exists(instance.SavePath));
    }

    [Fact]
    public void EnumerateGameboxes_returns_only_folders_with_a_profile()
    {
        var store = new GameboxStore();
        var dir = Path.Combine(Path.GetTempPath(), "emudos-tests", Guid.NewGuid().ToString("N"));
        var real = Path.Combine(dir, "game1");
        store.WriteProfile(real, new GameProfile { Title = "G1" });
        Directory.CreateDirectory(Path.Combine(dir, "not-a-gamebox"));

        var found = store.EnumerateGameboxes(dir).ToList();

        Assert.Equal([real], found);
    }

    [Fact]
    public void ReadProfile_throws_when_not_a_gamebox()
    {
        var store = new GameboxStore();
        var empty = Path.Combine(Path.GetTempPath(), "emudos-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(empty);

        Assert.Throws<FileNotFoundException>(() => store.ReadProfile(empty));
    }

    private static string TempBox() =>
        Path.Combine(Path.GetTempPath(), "emudos-tests", Guid.NewGuid().ToString("N"), "box");
}
