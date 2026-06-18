namespace EmuDOS.Core.Engine;

/// <summary>
/// What an engine can do. Lets higher layers adapt (e.g. hide save-state UI, or warn that
/// an exact cycle count will be approximated) without hard-coding engine identities.
/// </summary>
public sealed record EngineCapabilities
{
    public required string EngineId { get; init; }

    public bool SaveStates { get; init; }

    public bool Reset { get; init; }

    /// <summary>Can apply an exact, non-preset cycle count (dosbox_pure does this via DOSBOX.BAT).</summary>
    public bool ExactCycles { get; init; }

    /// <summary>Renders to a GPU surface rather than a CPU framebuffer.</summary>
    public bool HardwareRendered { get; init; }
}
