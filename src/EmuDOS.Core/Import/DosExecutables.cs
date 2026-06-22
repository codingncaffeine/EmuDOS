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

    /// <summary>
    /// Follow a launcher <c>.bat</c> that just runs an exe by a hardcoded path to the real exe in the
    /// content, when that path doesn't resolve (common in packaged/eXoDOS games whose .bat assumes a
    /// fixed install drive/folder like <c>C:\TOMBRAID\TOMB.EXE</c>, which breaks once the game is
    /// imported into a subfolder). Returns the content-relative path of the real exe, or the original
    /// path unchanged when the .bat isn't a broken redirector (its target resolves, it does real
    /// SET/PATH/MOUNT setup, or the exe can't be found). Lets such games launch with no manual fixup.
    /// </summary>
    public static string ResolveBatRedirect(string contentDir, string relPath)
    {
        if (string.IsNullOrEmpty(relPath) || !relPath.EndsWith(".bat", StringComparison.OrdinalIgnoreCase))
            return relPath;
        try
        {
            var lines = File.ReadLines(Path.Combine(contentDir, relPath))
                .Select(l => l.Trim().TrimStart('@').Trim())
                .Where(l => l.Length > 0
                         && !l.StartsWith("echo", StringComparison.OrdinalIgnoreCase)
                         && !l.StartsWith("rem", StringComparison.OrdinalIgnoreCase)
                         && !l.StartsWith("::"))
                .ToList();

            // A .bat that sets up the environment or mounts is a genuine launcher — leave it alone.
            if (lines.Any(l => l.StartsWith("set ", StringComparison.OrdinalIgnoreCase)
                            || l.StartsWith("path ", StringComparison.OrdinalIgnoreCase)
                            || l.Contains("mount", StringComparison.OrdinalIgnoreCase)))
                return relPath;

            var target = lines.Select(ExeToken).FirstOrDefault(t => t is not null);
            if (target is null)
                return relPath;

            var dosPath = target.Replace('/', '\\').TrimStart('\\');
            if (dosPath.Length > 1 && dosPath[1] == ':')   // strip a drive letter (C:\…)
                dosPath = dosPath[2..].TrimStart('\\');

            if (File.Exists(Path.Combine(contentDir, dosPath)))
                return relPath; // the hardcoded path is valid here — the .bat is fine

            var realExe = Directory
                .EnumerateFiles(contentDir, Path.GetFileName(dosPath), SearchOption.AllDirectories)
                .FirstOrDefault();
            return realExe is null ? relPath : Path.GetRelativePath(contentDir, realExe).Replace('/', '\\');
        }
        catch
        {
            return relPath;
        }
    }

    // The first .exe/.com token on a line (ignoring args / leading commands like CALL).
    private static string? ExeToken(string line) =>
        line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(t => t.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                              || t.EndsWith(".com", StringComparison.OrdinalIgnoreCase));

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
