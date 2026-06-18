namespace EmuDOS.Core.Model;

/// <summary>
/// How the game starts: which executable to run and any DOS commands to run first.
/// dosbox_pure has no "which exe" option, so the adapter realises this via a generated
/// DOSBOX.BAT inside the gamebox.
/// </summary>
public sealed record LaunchSpec
{
    /// <summary>Executable to run, relative to the primary (C:) mount root.</summary>
    public string? Executable { get; init; }

    public string? Arguments { get; init; }

    /// <summary>
    /// Extra DOS commands executed before launch (autoexec lines): SET, custom MOUNT,
    /// install/patch helpers, etc.
    /// </summary>
    public IReadOnlyList<string> PreCommands { get; init; } = [];
}
