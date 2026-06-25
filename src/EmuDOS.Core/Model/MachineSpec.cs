namespace EmuDOS.Core.Model;

/// <summary>Machine / video adapter settings.</summary>
public sealed record MachineSpec
{
    public MachineType Machine { get; init; } = MachineType.Svga;

    public SvgaChipset Svga { get; init; } = SvgaChipset.S3Trio64;

    /// <summary>SVGA video memory in KB (dosbox_pure exposes 512KB steps, 512KB–4MB).</summary>
    public int SvgaMemoryKb { get; init; } = 1024;

    /// <summary>Apply 4:3 aspect-ratio correction on output.</summary>
    public bool AspectCorrection { get; init; }

    /// <summary>Lock the output frame rate to this FPS (0 = off). Drives dosbox_pure's "Force Output
    /// FPS" — steadies the frame rate and removes tearing. Note: caps presentation, not the emulated
    /// CPU speed (use cycles for games that run too fast).</summary>
    public int FpsLock { get; init; }

    /// <summary>Per-game hardware-3dfx choice (Default follows the global setting).</summary>
    public Hardware3dfxMode Hardware3dfx { get; init; } = Hardware3dfxMode.Default;
}
