namespace EmuDOS.Core.Model;

/// <summary>
/// Memory settings. Note: dosbox_pure cannot split XMS and EMS (a single memory knob
/// enables/disables both), so the engine adapter maps these conservatively — but the
/// model records the curated intent faithfully (e.g. from an eXoDOS .conf seed).
/// </summary>
public sealed record MemorySpec
{
    public int SizeMb { get; init; } = 16;

    public bool Xms { get; init; } = true;

    public bool Ems { get; init; } = true;

    public bool Umb { get; init; } = true;
}
