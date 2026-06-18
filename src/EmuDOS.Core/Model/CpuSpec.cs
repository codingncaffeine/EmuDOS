namespace EmuDOS.Core.Model;

/// <summary>
/// CPU emulation settings. Engine-agnostic; the engine adapter maps these onto its own
/// knobs (e.g. dosbox_pure <c>cpu_type</c> / <c>cpu_core</c> / <c>cycles</c>).
/// </summary>
public sealed record CpuSpec
{
    public CpuType Type { get; init; } = CpuType.Auto;

    public CpuCore Core { get; init; } = CpuCore.Auto;

    public CyclesMode CyclesMode { get; init; } = CyclesMode.Auto;

    /// <summary>
    /// Exact cycle count, used only when <see cref="CyclesMode"/> is
    /// <see cref="Model.CyclesMode.Fixed"/>. dosbox_pure has no free-form cycles option,
    /// so a non-preset value is applied through a generated DOSBOX.BAT (<c>@CYCLES fixed N</c>).
    /// </summary>
    public int FixedCycles { get; init; }
}
