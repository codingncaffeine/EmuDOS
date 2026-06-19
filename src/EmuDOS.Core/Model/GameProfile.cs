namespace EmuDOS.Core.Model;

/// <summary>
/// The complete, engine-agnostic configuration that makes one DOS game "just work":
/// CPU/speed, machine, memory, sound, mounts, and what to launch.
/// <para>
/// This is the heart of EmuDOS. It is the canonical record serialized as
/// <c>profile.json</c> inside a gamebox (the source of truth); the library database is
/// only a rebuildable index over these. The engine is a detail that consumes a profile —
/// nothing here knows or cares which emulator runs it.
/// </para>
/// </summary>
public sealed record GameProfile
{
    /// <summary>Schema version of the on-disk <c>profile.json</c>, for forward compatibility.</summary>
    public int SchemaVersion { get; init; } = 1;

    public string Title { get; init; } = string.Empty;

    /// <summary>Stable identifier used to match against the curated catalog and metadata sources.</summary>
    public string? CanonicalId { get; init; }

    public SourceMediaType SourceMedia { get; init; } = SourceMediaType.Folder;

    public CpuSpec Cpu { get; init; } = new();

    public MachineSpec Machine { get; init; } = new();

    public MemorySpec Memory { get; init; } = new();

    public SoundSpec Sound { get; init; } = new();

    /// <summary>Frontend display tweaks (brightness/gamma) applied by EmuDOS, not the core.</summary>
    public DisplaySpec Display { get; init; } = new();

    public JoystickSpec Joystick { get; init; } = new();

    public LaunchSpec Launch { get; init; } = new();

    public IReadOnlyList<MountSpec> Mounts { get; init; } = [];

    /// <summary>Provenance, for the curated-base ← user-override layering.</summary>
    public ProfileOrigin Origin { get; init; } = ProfileOrigin.Default;
}
