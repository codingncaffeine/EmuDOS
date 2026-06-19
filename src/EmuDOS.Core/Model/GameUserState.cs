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

    /// <summary>Executables that have been run for this game, most-recent-first.</summary>
    public IReadOnlyList<string> KnownExecutables { get; init; } = [];

    /// <summary>Return a copy with <paramref name="executable"/> recorded as the most recent.</summary>
    public GameUserState WithExecutable(string? executable)
    {
        if (string.IsNullOrWhiteSpace(executable))
            return this;

        var known = new List<string> { executable };
        known.AddRange(KnownExecutables.Where(e =>
            !string.Equals(e, executable, StringComparison.OrdinalIgnoreCase)));

        return this with { LastExecutable = executable, KnownExecutables = known };
    }
}
