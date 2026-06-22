using System.IO.Compression;

namespace EmuDOS.Core.Library;

/// <summary>Local backup helpers: bundle every game's save data into one archive the user controls.</summary>
public static class SaveBackup
{
    /// <summary>
    /// Zip each gamebox's <c>saves/</c> folder and <c>notes.md</c> into a single archive, keyed by
    /// game-folder name. Returns the number of games that had something to back up.
    /// </summary>
    public static int CreateAllSavesArchive(string gameboxesDir, string destZip)
    {
        if (File.Exists(destZip))
            File.Delete(destZip);
        using var zip = ZipFile.Open(destZip, ZipArchiveMode.Create);

        int count = 0;
        if (!Directory.Exists(gameboxesDir))
            return 0;

        foreach (var gameDir in Directory.EnumerateDirectories(gameboxesDir))
        {
            var name = Path.GetFileName(gameDir);
            bool any = false;

            var savesDir = Path.Combine(gameDir, "saves");
            if (Directory.Exists(savesDir))
            {
                foreach (var f in Directory.EnumerateFiles(savesDir, "*", SearchOption.AllDirectories))
                {
                    var rel = Path.GetRelativePath(gameDir, f).Replace('\\', '/');
                    zip.CreateEntryFromFile(f, $"{name}/{rel}");
                    any = true;
                }
            }

            var notes = Path.Combine(gameDir, "notes.md");
            if (File.Exists(notes))
            {
                zip.CreateEntryFromFile(notes, $"{name}/notes.md");
                any = true;
            }

            if (any)
                count++;
        }
        return count;
    }
}
