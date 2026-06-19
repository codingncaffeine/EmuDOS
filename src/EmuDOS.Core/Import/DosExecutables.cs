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

    /// <summary>A DOS extender (the game's .bat launcher runs it; it's never the launch target).</summary>
    public static bool IsExtender(string path) => Extenders.Contains(Stem(path));

    /// <summary>An extender or emulator wrapper — never a launch target.</summary>
    public static bool IsRuntimeHelper(string path)
    {
        var stem = Stem(path);
        return Extenders.Contains(stem) || Wrappers.Contains(stem);
    }

    private static string Stem(string path) =>
        Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
}
