namespace EmuDOS.Core.Model;

/// <summary>
/// Per-game user state that isn't part of the curated profile: the last window size, and the
/// executables that have actually been run (so we can default to a working one and offer the
/// rest in a menu — the way Boxer remembered programs). Lives as state.json in the gamebox.
/// </summary>
public sealed record GameUserState
{
    public int? WindowWidth { get; init; }

    public int? WindowHeight { get; init; }

    /// <summary>The executable last launched (DOS path relative to the C: mount), if any.</summary>
    public string? LastExecutable { get; init; }

    /// <summary>The program the user last launched from the DOS prompt (what they actually ran to
    /// play), captured automatically. Beats auto-detection but loses to a deliberate picker choice.</summary>
    public string? LastRunProgram { get; init; }

    /// <summary>Executables that have been run for this game, most-recent-first.</summary>
    public IReadOnlyList<string> KnownExecutables { get; init; } = [];

    /// <summary>True when <see cref="LastExecutable"/> was a deliberate choice (the program picker),
    /// so it should win over auto-detection. When false, auto-detecting the game is preferred.</summary>
    public bool ExecutableIsUserChoice { get; init; }

    /// <summary>Per-game box-art style override. <see cref="BoxStyle.Default"/> follows the global
    /// <c>UserSettings.Use3DBoxes</c> preference; the others force 2D or 3D for this game alone.</summary>
    public BoxStyle BoxStyle { get; init; } = BoxStyle.Default;

    /// <summary>Per-game CRT shader override ("Off"/"Scanlines"/"Crt"/"Green"/"Amber"). Empty follows
    /// the global <c>UserSettings.VideoShader</c> default.</summary>
    public string Shader { get; init; } = "";

    /// <summary>Return a copy with <paramref name="executable"/> recorded as the user's chosen program.</summary>
    public GameUserState WithExecutable(string? executable)
    {
        if (string.IsNullOrWhiteSpace(executable))
            return this;

        var known = new List<string> { executable };
        known.AddRange(KnownExecutables.Where(e =>
            !string.Equals(e, executable, StringComparison.OrdinalIgnoreCase)));

        return this with { LastExecutable = executable, KnownExecutables = known, ExecutableIsUserChoice = true };
    }
}
