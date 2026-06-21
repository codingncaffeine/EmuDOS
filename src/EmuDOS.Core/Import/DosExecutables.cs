namespace EmuDOS.Core.Import;

/// <summary>
/// Classifies DOS executables that are NOT a game's launch target: DOS extenders (DOS4GW and
/// friends — runtime helpers a .bat invokes) and emulator wrappers. Used so import doesn't guess
/// an extender as the program to run, and so the Run menu doesn't list noise.
/// </summary>
public static class DosExecutables
{
    private static readonly string[] Extenders =
        ["dos4gw", "dos4g", "dos32a", "dos32", "pmodew", "pmode", "cwsdpmi", "dpmi", "dpmiload", "rtm"];

    private static readonly string[] Wrappers = ["dosbox", "4dos"];

    // Filenames that are the canonical launch target across many games, so prefer them as the
    // default when present — even over the largest-exe / extender-.bat guesses. Sierra SCI games
    // boot via SIERRA.EXE (older ones via SCIV/SCIW/SCIDHUV); repackaged sets commonly ship a
    // run/start/play/go launcher.
    private static readonly string[] Launchers =
        ["sierra", "sciv", "sciw", "scidhuv", "run", "runme", "start", "play", "game", "go"];

    // Support tools that ship alongside a game but are never the game itself. Used to push them
    // below real candidates when guessing the launch target (e.g. a big "DVD Prep Wizard.exe"
    // shouldn't outweigh SIERRA.EXE just because it's larger).
    private static readonly string[] UtilityMarkers =
        ["wizard", "prep", "patch", "uninst", "regist", "order", "help", "readme", "manual", "demo"];

    /// <summary>A DOS extender (the game's .bat launcher runs it; it's never the launch target).</summary>
    public static bool IsExtender(string path) => Extenders.Contains(Stem(path));

    /// <summary>A well-known canonical launcher filename — the best default when one is present.</summary>
    public static bool IsKnownLauncher(string path) => Launchers.Contains(Stem(path));

    /// <summary>Looks like a bundled support tool (patcher, registration, readme viewer …), so it's
    /// a poor launch guess — deprioritise it behind real candidates.</summary>
    public static bool IsLikelyUtility(string path)
    {
        var stem = Stem(path);
        return UtilityMarkers.Any(m => stem.Contains(m));
    }

    private static readonly string[] FillerWords = ["the", "of", "and", "a", "an", "to", "in"];

    /// <summary>Whether an executable's filename plausibly names the game, allowing for the common
    /// DOS abbreviations: an exact title word, a substring either way, the title's initials, or the
    /// first title word as a prefix (so an abbreviated or truncated exe name still matches the full
    /// title). Length-guarded so short coincidences don't match. Deterministic — no fuzzy distance.</summary>
    public static bool TitleMatches(string fileName, string title)
    {
        static string Norm(string s) =>
            new(s.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

        var stem = Norm(Path.GetFileNameWithoutExtension(fileName));
        if (stem.Length < 2)
            return false;

        var words = title
            .Split([' ', '_', '-', '.', '(', ')', '[', ']', ':', '\'', ','], StringSplitOptions.RemoveEmptyEntries)
            .Select(Norm)
            .Where(w => w.Length > 0 && !FillerWords.Contains(w))
            .ToList();
        if (words.Count == 0)
            return false;

        if (words.Contains(stem))
            return true; // an exact title word

        var key = string.Concat(words);
        if (key.Length >= 3 && stem.Length >= 3 && (stem.Contains(key) || key.Contains(stem)))
            return true; // substring either way (exe name contains, or is contained by, the title)

        var acronym = string.Concat(words.Select(w => w[0]));
        if (acronym.Length >= 3 && stem == acronym)
            return true; // the title's initials

        return words[0].Length >= 4 && stem.StartsWith(words[0], StringComparison.Ordinal);
        // the first title word as a prefix (an abbreviated/truncated exe name)
    }

    /// <summary>An extender or emulator wrapper — never a launch target.</summary>
    public static bool IsRuntimeHelper(string path)
    {
        var stem = Stem(path);
        return Extenders.Contains(stem) || Wrappers.Contains(stem);
    }

    private static string Stem(string path) =>
        Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
}
