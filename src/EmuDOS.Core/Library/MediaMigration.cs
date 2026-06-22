using System.Text.RegularExpressions;

namespace EmuDOS.Core.Library;

/// <summary>
/// One-time move of screenshots/videos from the old flat global folders into each gamebox's
/// per-game <c>media/screenshots</c> and <c>media/videos</c>. Files were named
/// "<c>{SafeName(Title)} {timestamp}.ext</c>", so we recover the title and match it to a gamebox.
/// Unmatched files are left in place. Runs once (guarded by a marker file); idempotent regardless.
/// </summary>
public static partial class MediaMigration
{
    public static int Run(string markerFile, string flatScreenshots, string flatVideos,
                          IEnumerable<(string Title, string Root)> games)
    {
        if (File.Exists(markerFile))
            return 0;

        // Map SafeName(title) -> gamebox root. First match wins on duplicate titles.
        var byName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (title, root) in games)
            byName.TryAdd(SafeName(title), root);

        int moved = 0;
        moved += MoveMatching(flatScreenshots, ".png", byName, root => new Gamebox(root).ScreenshotsDir);
        moved += MoveMatching(flatVideos, ".mp4", byName, root => new Gamebox(root).VideosDir);

        try
        {
            File.WriteAllText(markerFile, DateTime.UtcNow.ToString("o"));
        }
        catch { /* a failed marker just means we retry next launch — harmless */ }
        return moved;
    }

    private static int MoveMatching(string flatDir, string ext, Dictionary<string, string> byName,
                                    Func<string, string> destDirFor)
    {
        if (!Directory.Exists(flatDir))
            return 0;

        int moved = 0;
        foreach (var file in Directory.EnumerateFiles(flatDir, "*" + ext))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            // Strip a trailing " yyyy-MM-dd HH-mm-ss" to recover the SafeName(title) prefix.
            var key = TimestampSuffix().Replace(name, "");
            if (!byName.TryGetValue(key, out var root))
                continue;
            try
            {
                var destDir = destDirFor(root);
                Directory.CreateDirectory(destDir);
                var dest = Path.Combine(destDir, Path.GetFileName(file));
                if (!File.Exists(dest))
                {
                    File.Move(file, dest);
                    moved++;
                }
            }
            catch { /* skip a file we can't move; leave it in the flat folder */ }
        }
        return moved;
    }

    private static string SafeName(string title) =>
        string.Concat(title.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim();

    [GeneratedRegex(@"\s\d{4}-\d{2}-\d{2}\s\d{2}-\d{2}-\d{2}$")]
    private static partial Regex TimestampSuffix();
}
