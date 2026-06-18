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
}
