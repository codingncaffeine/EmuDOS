using System.Globalization;
using EmuDOS.Core.Model;

namespace EmuDOS.Core.Catalog;

/// <summary>
/// Parses a standard DOSBox <c>.conf</c> (the format eXoDOS and GOG ship per game) into a
/// <see cref="GameProfile"/>. This is how curated per-game knowledge becomes our engine-
/// agnostic model — the inverse of <c>DosBoxPureAdapter</c>. The <c>[autoexec]</c> block is
/// captured as launch pre-commands (it already mounts and starts the game), so the resulting
/// profile leaves <c>Launch.Executable</c> empty.
/// </summary>
public static class DosBoxConfParser
{
    public static GameProfile Parse(string confText, string title = "")
    {
        ArgumentNullException.ThrowIfNull(confText);
        var (sections, autoexec) = ReadIni(confText);

        return new GameProfile
        {
            Title = title,
            Origin = ProfileOrigin.CuratedBase,
            Machine = ParseMachine(sections),
            Cpu = ParseCpu(sections),
            Memory = ParseMemory(sections),
            Sound = ParseSound(sections),
            Joystick = ParseJoystick(sections),
            Launch = new LaunchSpec { PreCommands = autoexec },
        };
    }

    private static MachineSpec ParseMachine(Sections s)
    {
        var machineRaw = (Get(s, "dosbox", "machine") ?? "svga_s3").ToLowerInvariant();
        var (machine, chipset) = machineRaw switch
        {
            "svga_s3" => (MachineType.Svga, SvgaChipset.S3Trio64),
            "svga_et3000" => (MachineType.Svga, SvgaChipset.Et3000),
            "svga_et4000" => (MachineType.Svga, SvgaChipset.Et4000),
            "svga_paradise" => (MachineType.Svga, SvgaChipset.Paradise),
            "vesa_nolfb" => (MachineType.Svga, SvgaChipset.VesaNoLfb),
            "vesa_oldvbe" => (MachineType.Svga, SvgaChipset.VesaOldVbe),
            "vgaonly" or "vga" => (MachineType.Vga, SvgaChipset.S3Trio64),
            "ega" => (MachineType.Ega, SvgaChipset.S3Trio64),
            "cga" => (MachineType.Cga, SvgaChipset.S3Trio64),
            "tandy" => (MachineType.Tandy, SvgaChipset.S3Trio64),
            "hercules" or "herc" => (MachineType.Hercules, SvgaChipset.S3Trio64),
            "pcjr" => (MachineType.PcJr, SvgaChipset.S3Trio64),
            _ => (MachineType.Svga, SvgaChipset.S3Trio64),
        };

        var memKb = ParseInt(Get(s, "dosbox", "memsizekb"));
        var spec = new MachineSpec { Machine = machine, Svga = chipset };
        return memKb > 0 ? spec with { SvgaMemoryKb = memKb } : spec;
    }

    private static CpuSpec ParseCpu(Sections s)
    {
        var core = (Get(s, "cpu", "core") ?? "auto").ToLowerInvariant() switch
        {
            "dynamic" or "dynamic_x86" or "dynamic_rec" => CpuCore.Dynamic,
            "normal" => CpuCore.Normal,
            "simple" => CpuCore.Simple,
            _ => CpuCore.Auto,
        };

        var type = (Get(s, "cpu", "cputype") ?? "auto").ToLowerInvariant() switch
        {
            "386" => CpuType.I386,
            "386_slow" => CpuType.I386Slow,
            "386_prefetch" => CpuType.I386Prefetch,
            "486" or "486_slow" => CpuType.I486Slow,
            "pentium" or "pentium_slow" => CpuType.PentiumSlow,
            "pentium_mmx" => CpuType.PentiumMmx,
            _ => CpuType.Auto,
        };

        var (mode, fixedCycles) = ParseCycles(Get(s, "cpu", "cycles"));
        return new CpuSpec { Core = core, Type = type, CyclesMode = mode, FixedCycles = fixedCycles };
    }

    private static (CyclesMode, int) ParseCycles(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return (CyclesMode.Auto, 0);

        var value = raw.Trim().ToLowerInvariant();
        if (value.StartsWith("max"))
            return (CyclesMode.Max, 0);
        if (value.StartsWith("auto"))
            return (CyclesMode.Auto, 0);

        // "fixed 20000" or a bare "20000".
        var token = value.StartsWith("fixed")
            ? value[5..].Trim().Split(' ').FirstOrDefault()
            : value.Split(' ').FirstOrDefault();
        return int.TryParse(token, out var n) ? (CyclesMode.Fixed, n) : (CyclesMode.Auto, 0);
    }

    private static MemorySpec ParseMemory(Sections s)
    {
        var size = ParseInt(Get(s, "dosbox", "memsize"));
        return new MemorySpec
        {
            SizeMb = size > 0 ? size : 16,
            Xms = ParseBool(Get(s, "dos", "xms"), true),
            Ems = ParseBool(Get(s, "dos", "ems"), true),
            Umb = ParseBool(Get(s, "dos", "umb"), true),
        };
    }

    private static SoundSpec ParseSound(Sections s)
    {
        var sb = (Get(s, "sblaster", "sbtype") ?? "sb16").ToLowerInvariant() switch
        {
            "sbpro2" => SoundBlasterType.SbPro2,
            "sbpro1" => SoundBlasterType.SbPro1,
            "sb2" => SoundBlasterType.Sb2,
            "sb1" => SoundBlasterType.Sb1,
            "gb" => SoundBlasterType.GameBlaster,
            "none" => SoundBlasterType.None,
            _ => SoundBlasterType.Sb16,
        };

        var adlib = (Get(s, "sblaster", "oplmode") ?? "auto").ToLowerInvariant() switch
        {
            "cms" => AdlibMode.Cms,
            "opl2" => AdlibMode.Opl2,
            "dualopl2" => AdlibMode.DualOpl2,
            "opl3" => AdlibMode.Opl3,
            "opl3gold" => AdlibMode.Opl3Gold,
            "none" => AdlibMode.None,
            _ => AdlibMode.Auto,
        };

        var midi = (Get(s, "midi", "mididevice") ?? "default").ToLowerInvariant() switch
        {
            "mt32" => MidiDevice.Mt32,
            "fluidsynth" => MidiDevice.SoundFont,
            "none" => MidiDevice.None,
            _ => MidiDevice.GeneralMidi,
        };

        return new SoundSpec
        {
            SoundBlaster = sb,
            Port = ParseHex(Get(s, "sblaster", "sbbase"), 0x220),
            Irq = ParseInt(Get(s, "sblaster", "irq"), 7),
            LowDma = ParseInt(Get(s, "sblaster", "dma"), 1),
            HighDma = ParseInt(Get(s, "sblaster", "hdma"), 5),
            Adlib = adlib,
            GravisUltrasound = ParseBool(Get(s, "gus", "gus"), false),
            Midi = midi,
        };
    }

    private static JoystickSpec ParseJoystick(Sections s)
    {
        var type = (Get(s, "joystick", "joysticktype") ?? "auto").ToLowerInvariant() switch
        {
            "none" => JoystickType.None,
            "2axis" => JoystickType.TwoAxis,
            "4axis" => JoystickType.FourAxis,
            "4axis_2" => JoystickType.FourAxis2,
            "fcs" => JoystickType.Fcs,
            "ch" => JoystickType.Ch,
            _ => JoystickType.Auto,
        };
        return new JoystickSpec { Type = type };
    }

    private static (Sections, List<string>) ReadIni(string text)
    {
        var sections = new Sections();
        var autoexec = new List<string>();
        var current = string.Empty;

        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
                continue;

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                current = line[1..^1].Trim().ToLowerInvariant();
                continue;
            }

            if (current == "autoexec")
            {
                if (!line.StartsWith('#') && !line.StartsWith(';'))
                    autoexec.Add(line);
                continue;
            }

            if (line.StartsWith('#') || line.StartsWith(';'))
                continue;

            var eq = line.IndexOf('=');
            if (eq < 0)
                continue;

            var key = line[..eq].Trim().ToLowerInvariant();
            var val = line[(eq + 1)..].Trim();
            if (!sections.TryGetValue(current, out var dict))
                sections[current] = dict = new(StringComparer.OrdinalIgnoreCase);
            dict[key] = val;
        }

        return (sections, autoexec);
    }

    private static string? Get(Sections s, string section, string key) =>
        s.TryGetValue(section, out var dict) && dict.TryGetValue(key, out var v) ? v : null;

    private static int ParseInt(string? s, int fallback = 0) =>
        int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : fallback;

    private static int ParseHex(string? s, int fallback) =>
        int.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var n) ? n : fallback;

    private static bool ParseBool(string? s, bool fallback) =>
        bool.TryParse(s, out var b) ? b : fallback;

    private sealed class Sections : Dictionary<string, Dictionary<string, string>>
    {
        public Sections() : base(StringComparer.OrdinalIgnoreCase) { }
    }
}
