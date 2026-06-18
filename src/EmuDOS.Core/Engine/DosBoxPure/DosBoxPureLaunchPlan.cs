namespace EmuDOS.Core.Engine.DosBoxPure;

/// <summary>
/// Everything the dosbox_pure core needs to run a game the way its <c>GameProfile</c>
/// intends: the core-option values to answer <c>GET_VARIABLE</c> with, and the contents of
/// a <c>DOSBOX.BAT</c> to drop into the mounted content (dosbox_pure runs it automatically,
/// skipping the start menu).
/// </summary>
public sealed record DosBoxPureLaunchPlan
{
    /// <summary>dosbox_pure_* option key → value, returned to the core on demand.</summary>
    public required IReadOnlyDictionary<string, string> CoreOptions { get; init; }

    /// <summary>
    /// Contents of the generated <c>DOSBOX.BAT</c>. Carries everything core options can't
    /// express: exact (non-preset) cycles, EMS/XMS overrides, autoexec mounts, and the
    /// executable to launch.
    /// </summary>
    public required string AutoexecBat { get; init; }
}
