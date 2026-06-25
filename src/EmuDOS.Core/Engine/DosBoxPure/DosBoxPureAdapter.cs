using System.Globalization;
using System.IO;
using System.Text;
using EmuDOS.Core.Model;

namespace EmuDOS.Core.Engine.DosBoxPure;

/// <summary>
/// Translates an engine-agnostic <see cref="GameProfile"/> into dosbox_pure's actual knobs.
/// This is the one place that knows dosbox_pure's option names, value vocabulary, and its
/// limitations (preset-only cycles / SB resources, no EMS-XMS split) — and how to work
/// around them via a generated <c>DOSBOX.BAT</c>.
/// </summary>
public static class DosBoxPureAdapter
{
    // dosbox_pure exposes only these cycle counts; an exact value is applied via DOSBOX.BAT.
    private static readonly int[] CyclePresets =
        [315, 1320, 2750, 4720, 7800, 13400, 26800, 77000, 200000, 500000, 1000000];

    private static readonly int[] MemoryPresets =
        [4, 8, 16, 24, 32, 48, 64, 96, 128, 224, 256, 512, 1024];

    private static readonly int[] AudioRates =
        [8000, 11025, 16000, 22050, 32000, 44100, 48000, 49716];

    // The ten fixed Sound Blaster resource presets dosbox_pure accepts (sblaster_conf).
    private static readonly (string Conf, int Port, int Irq, int LowDma, int HighDma)[] SbPresets =
    [
        ("A220 I7 D1 H5", 0x220, 7, 1, 5),
        ("A220 I5 D1 H5", 0x220, 5, 1, 5),
        ("A240 I7 D1 H5", 0x240, 7, 1, 5),
        ("A240 I7 D3 H7", 0x240, 7, 3, 7),
        ("A240 I2 D3 H7", 0x240, 2, 3, 7),
        ("A240 I5 D3 H5", 0x240, 5, 3, 5),
        ("A240 I5 D1 H5", 0x240, 5, 1, 5),
        ("A240 I10 D3 H7", 0x240, 10, 3, 7),
        ("A280 I10 D0 H6", 0x280, 10, 0, 6),
        ("A280 I5 D1 H5", 0x280, 5, 1, 5),
    ];

    /// <summary>Build the full set of core options + the DOSBOX.BAT for a profile.</summary>
    public static DosBoxPureLaunchPlan BuildLaunchPlan(GameProfile profile, string? contentDir = null)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return new DosBoxPureLaunchPlan
        {
            CoreOptions = BuildCoreOptions(profile, contentDir),
            AutoexecBat = BuildAutoexecBat(profile),
        };
    }

    /// <summary>Map a profile to dosbox_pure_* core-option values. <paramref name="contentDir"/> lets
    /// the start-menu behavior adapt once a CD game has been installed.</summary>
    public static IReadOnlyDictionary<string, string> BuildCoreOptions(GameProfile profile, string? contentDir = null)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var o = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["dosbox_pure_machine"] = MachineValue(profile.Machine.Machine),
            ["dosbox_pure_cpu_type"] = CpuTypeValue(profile.Cpu.Type),
            ["dosbox_pure_cpu_core"] = CpuCoreValue(profile.Cpu.Core),
            ["dosbox_pure_cycles"] = CyclesOption(profile.Cpu),
            ["dosbox_pure_memory_size"] = MemoryValue(profile.Memory),
            ["dosbox_pure_sblaster_type"] = SbTypeValue(profile.Sound.SoundBlaster),
            ["dosbox_pure_sblaster_conf"] = SnapSoundBlasterConf(
                profile.Sound.Port, profile.Sound.Irq, profile.Sound.LowDma, profile.Sound.HighDma),
            ["dosbox_pure_sblaster_adlib_mode"] = AdlibValue(profile.Sound.Adlib),
            ["dosbox_pure_gus"] = Bool(profile.Sound.GravisUltrasound),
            ["dosbox_pure_tandysound"] = profile.Sound.TandySound ? "on" : "auto",
            ["dosbox_pure_audiorate"] = NearestPreset(profile.Sound.AudioRateHz, AudioRates)
                .ToString(CultureInfo.InvariantCulture),
            ["dosbox_pure_midi"] = MidiValue(profile.Sound),
        };

        if (profile.Machine.Machine == MachineType.Svga)
        {
            o["dosbox_pure_svga"] = SvgaValue(profile.Machine.Svga);
            o["dosbox_pure_svgamem"] = SvgaMemValue(profile.Machine.SvgaMemoryKb);
        }

        // Per-game frame-rate lock → dosbox_pure's "Force Output FPS" (0 = leave at default).
        if (profile.Machine.FpsLock > 0)
            o["dosbox_pure_force60fps"] = profile.Machine.FpsLock.ToString(CultureInfo.InvariantCulture);

        // Disc images load as content with no autoexec, so force the core's start menu to stay open
        // (-1) — that's where "Boot and Install New Operating System" + the hard-disk size live.
        // Normal games auto-run via DOSBOX.BAT, so they keep the default (auto-start) behavior.
        // BUT once a CD game has been installed, the core writes an AUTOBOOT.DBP recording what to run;
        // from then on, drop the forced menu so the core auto-starts that target instead of trapping
        // the user at the menu every launch.
        if (profile.SourceMedia == SourceMediaType.Iso)
        {
            var installed = contentDir is not null && File.Exists(Path.Combine(contentDir, "AUTOBOOT.DBP"));
            if (!installed)
                o["dosbox_pure_menu_time"] = "-1";
            // When booting an installed OS, don't create the empty writable D: scratch drive — it
            // shows up confusingly labelled after our .m3u8. Use only the mounted CD-ROM(s) instead,
            // so a game disc is the drive the OS sees. (No effect on plain DOS CD games.)
            o["dosbox_pure_bootos_dfreespace"] = "hide";
        }

        return o;
    }

    /// <summary>
    /// Generate the DOSBOX.BAT autoexec: EMS/XMS overrides, exact cycles, the curated
    /// pre-commands (mounts etc.), then the executable launch.
    /// </summary>
    public static string BuildAutoexecBat(GameProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var sb = new StringBuilder();
        sb.AppendLine("@ECHO OFF");

        // NOTE: we deliberately do NOT emit "@config -set dos ems/xms/umb=false" here. Those DOS
        // memory settings are live (Changeable::WhenIdle), so applying them in the autoexec takes
        // effect WHILE the game is starting — yanking memory out from under it and faulting it
        // ("illegal operation", or a fall-through into another .bat). The curated seed disables EMS
        // aggressively and it rarely helps; a clean launch wins. (Memory *size* is still applied via
        // the dosbox_pure_memory_size core option, which is set at boot.)

        // Gameport joystick type: set the emulated stick the game expects (Auto = default).
        if (profile.Joystick.Type != JoystickType.Auto)
            sb.AppendLine($"@config -set \"joystick joysticktype={JoystickValue(profile.Joystick.Type)}\"");

        // Exact (non-preset) cycle counts are only reachable from inside autoexec.
        if (profile.Cpu.CyclesMode == CyclesMode.Fixed && profile.Cpu.FixedCycles > 0)
            sb.AppendLine(CultureInvariant($"@CYCLES fixed {profile.Cpu.FixedCycles}"));

        // Curated autoexec lines (from e.g. an eXoDOS seed) — typically MOUNT/IMGMOUNT/SET.
        foreach (var cmd in profile.Launch.PreCommands)
        {
            if (!string.IsNullOrWhiteSpace(cmd))
                sb.AppendLine(cmd.Trim());
        }

        if (!string.IsNullOrWhiteSpace(profile.Launch.Executable))
        {
            var exe = profile.Launch.Executable!.Trim().Replace('/', '\\');
            int slash = exe.LastIndexOf('\\');
            var dir = slash >= 0 ? exe[..slash].Trim('\\') : string.Empty;
            var file = slash >= 0 ? exe[(slash + 1)..] : exe;

            // Run the game FROM its own directory — most DOS games look for their data files in the
            // current directory, so one installed into a subfolder (e.g. ABUSE\ABUSE.EXE) must be
            // launched after cd-ing into it, not by path from C:\.
            if (dir.Length > 0)
                sb.AppendLine("@CD \\" + dir);

            var line = file;
            if (!string.IsNullOrWhiteSpace(profile.Launch.Arguments))
                line += " " + profile.Launch.Arguments!.Trim();
            sb.AppendLine("@" + line);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Pick the dosbox_pure <c>sblaster_conf</c> preset closest to the requested resources.
    /// Port match dominates, then IRQ, then the DMA channels.
    /// </summary>
    public static string SnapSoundBlasterConf(int port, int irq, int lowDma, int highDma)
    {
        var best = SbPresets[0];
        var bestScore = int.MaxValue;
        foreach (var p in SbPresets)
        {
            var score = (p.Port == port ? 0 : 1000)
                + Math.Abs(p.Irq - irq) * 10
                + Math.Abs(p.LowDma - lowDma)
                + Math.Abs(p.HighDma - highDma);
            if (score < bestScore)
            {
                bestScore = score;
                best = p;
            }
        }

        return best.Conf;
    }

    /// <summary>Nearest value in a preset list (by absolute difference).</summary>
    public static int NearestPreset(int value, ReadOnlySpan<int> presets)
    {
        var best = presets[0];
        var bestDiff = Math.Abs(presets[0] - value);
        foreach (var p in presets)
        {
            var diff = Math.Abs(p - value);
            if (diff < bestDiff)
            {
                bestDiff = diff;
                best = p;
            }
        }

        return best;
    }

    private static string CyclesOption(CpuSpec cpu) => cpu.CyclesMode switch
    {
        CyclesMode.Auto => "auto",
        CyclesMode.Max => "max",
        // Set the option to the nearest preset; DOSBOX.BAT then pins the exact value.
        CyclesMode.Fixed => NearestPreset(cpu.FixedCycles, CyclePresets)
            .ToString(CultureInfo.InvariantCulture),
        _ => "auto",
    };

    private static string MemoryValue(MemorySpec memory) =>
        memory.SizeMb <= 0
            ? "none"
            : NearestPreset(memory.SizeMb, MemoryPresets).ToString(CultureInfo.InvariantCulture);

    private static string MidiValue(SoundSpec sound)
    {
        // An explicit SoundFont file wins (the core's TSF plays it).
        if (!string.IsNullOrWhiteSpace(sound.MidiSoundFont))
            return sound.MidiSoundFont!;

        // Otherwise MT-32 / General MIDI route to the libretro frontend driver — EmuDOS
        // synthesizes them with its own MT-32 and reads the LCD from the stream.
        return sound.Midi switch
        {
            MidiDevice.Mt32 => "frontend",
            MidiDevice.GeneralMidi => "frontend",
            _ => "disabled",
        };
    }

    private static string SvgaMemValue(int kb) =>
        Math.Clamp(kb / 512, 0, 8).ToString(CultureInfo.InvariantCulture);

    private static string MachineValue(MachineType m) => m switch
    {
        MachineType.Svga => "svga",
        MachineType.Vga => "vga",
        MachineType.Ega => "ega",
        MachineType.Cga => "cga",
        MachineType.Tandy => "tandy",
        MachineType.Hercules => "hercules",
        MachineType.PcJr => "pcjr",
        _ => "svga",
    };

    private static string SvgaValue(SvgaChipset s) => s switch
    {
        SvgaChipset.S3Trio64 => "svga_s3",
        SvgaChipset.Et3000 => "svga_et3000",
        SvgaChipset.Et4000 => "svga_et4000",
        SvgaChipset.Paradise => "svga_paradise",
        SvgaChipset.VesaNoLfb => "vesa_nolfb",
        SvgaChipset.VesaOldVbe => "vesa_oldvbe",
        _ => "svga_s3",
    };

    private static string CpuTypeValue(CpuType t) => t switch
    {
        CpuType.Auto => "auto",
        CpuType.I386 => "386",
        CpuType.I386Slow => "386_slow",
        CpuType.I386Prefetch => "386_prefetch",
        CpuType.I486Slow => "486_slow",
        CpuType.PentiumSlow => "pentium_slow",
        CpuType.PentiumMmx => "pentium_mmx",
        _ => "auto",
    };

    private static string CpuCoreValue(CpuCore c) => c switch
    {
        CpuCore.Auto => "auto",
        CpuCore.Dynamic => "dynamic",
        CpuCore.Normal => "normal",
        CpuCore.Simple => "simple",
        _ => "auto",
    };

    private static string SbTypeValue(SoundBlasterType t) => t switch
    {
        SoundBlasterType.Sb16 => "sb16",
        SoundBlasterType.SbPro2 => "sbpro2",
        SoundBlasterType.SbPro1 => "sbpro1",
        SoundBlasterType.Sb2 => "sb2",
        SoundBlasterType.Sb1 => "sb1",
        SoundBlasterType.GameBlaster => "gb",
        SoundBlasterType.None => "none",
        _ => "sb16",
    };

    private static string AdlibValue(AdlibMode a) => a switch
    {
        AdlibMode.Auto => "auto",
        AdlibMode.Cms => "cms",
        AdlibMode.Opl2 => "opl2",
        AdlibMode.DualOpl2 => "dualopl2",
        AdlibMode.Opl3 => "opl3",
        AdlibMode.Opl3Gold => "opl3gold",
        AdlibMode.None => "none",
        _ => "auto",
    };

    private static string JoystickValue(JoystickType t) => t switch
    {
        JoystickType.None => "none",
        JoystickType.TwoAxis => "2axis",
        JoystickType.FourAxis => "4axis",
        JoystickType.FourAxis2 => "4axis_2",
        JoystickType.Fcs => "fcs",
        JoystickType.Ch => "ch",
        _ => "auto",
    };

    private static string Bool(bool b) => b ? "true" : "false";

    private static string CultureInvariant(FormattableString s) =>
        s.ToString(CultureInfo.InvariantCulture);
}
