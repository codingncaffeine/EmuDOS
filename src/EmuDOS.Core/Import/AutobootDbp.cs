using System;
using System.IO;
using System.Linq;

namespace EmuDOS.Core.Import;

/// <summary>
/// Reads a dosbox_pure <c>AUTOBOOT.DBP</c> — written when "set auto start" is chosen in the core's start
/// menu — which records the program to auto-run (e.g. <c>C:\ID\T7G\T7G.BAT</c>) plus a wait time on a
/// second line. Its presence means the content is a pre-installed game meant to be mounted as C: and
/// launched directly, not a raw disc to install. Honoring it stops such games (often nested alongside
/// their CD image, where the heuristic exe scan misses them) from being mistaken for a raw disc and
/// dropped at dosbox_pure's start menu.
/// </summary>
public static class AutobootDbp
{
    /// <summary>The auto-run target as a content-relative executable (the C: path with its drive
    /// stripped), or null when there's no AUTOBOOT.DBP, it points off the C: content (e.g. a D:\ CD
    /// target), or the named file isn't actually present.</summary>
    public static string? TryParseExecutable(string contentDir)
    {
        var path = Path.Combine(contentDir, "AUTOBOOT.DBP");
        if (!File.Exists(path))
            return null;

        var first = File.ReadLines(path).FirstOrDefault()?.Trim();
        if (string.IsNullOrEmpty(first))
            return null;

        // Only honor a target on the C: content drive — a D:\ target is a raw-CD auto-run, not an install.
        if (!first.StartsWith(@"C:\", StringComparison.OrdinalIgnoreCase))
            return null;
        var rel = first[3..].TrimStart('\\');
        if (rel.Length == 0)
            return null;

        // Sanity: the target must actually exist in the imported content.
        var full = Path.Combine(contentDir, rel.Replace('\\', Path.DirectorySeparatorChar));
        return File.Exists(full) ? rel : null;
    }
}
