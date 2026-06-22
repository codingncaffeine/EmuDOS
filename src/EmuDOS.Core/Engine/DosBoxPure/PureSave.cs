using System.IO.Compression;

namespace EmuDOS.Core.Engine.DosBoxPure;

/// <summary>
/// Reads and updates dosbox_pure's persisted C: drive — a standard ZIP (<c>*.pure.zip</c>) in the
/// game's save folder. For a CD game the disc mounts as D: and C: starts empty, so this ZIP is
/// effectively the whole installed C: drive.
/// <para>
/// This lets EmuDOS see what a CD installer put on C: (which otherwise lives only inside dosbox_pure's
/// save, invisible to the content-folder scan) and pin one program to launch automatically via
/// <c>AUTOBOOT.DBP</c> — dosbox_pure then boots straight into it with the disc still mounted as D:,
/// so games that check for the CD or stream CD audio keep working during play.
/// </para>
/// </summary>
public static class PureSave
{
    private const string AutoBootEntry = "AUTOBOOT.DBP";
    private static readonly string[] ExecutableExtensions = [".exe", ".com", ".bat"];

    /// <summary>The dosbox_pure C: save ZIP in this game's save folder, or null if none exists yet
    /// (i.e. the game hasn't been run/installed).</summary>
    public static string? FindSaveZip(string saveDir) =>
        Directory.Exists(saveDir)
            ? Directory.EnumerateFiles(saveDir, "*.pure.zip").FirstOrDefault()
            : null;

    /// <summary>Programs installed on the persisted C:, as DOS paths (e.g. <c>C:\WAR2\WAR2.EXE</c>).
    /// Empty if there's no save yet or it can't be read.</summary>
    public static List<string> ListInstalledExecutables(string saveDir)
    {
        var zip = FindSaveZip(saveDir);
        if (zip is null)
            return [];
        try
        {
            using var archive = ZipFile.OpenRead(zip);
            return archive.Entries
                .Where(e => e.Length > 0
                         && ExecutableExtensions.Contains(Path.GetExtension(e.FullName).ToLowerInvariant()))
                .Select(e => "C:\\" + e.FullName.Replace('/', '\\'))
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    /// <summary>Whether a path is one on the persisted C: drive (vs a file in the content folder).</summary>
    public static bool IsInstalledPath(string path) =>
        path.StartsWith("C:\\", StringComparison.OrdinalIgnoreCase);

    /// <summary>Pin a program on the persisted C: to auto-run on the next launch by writing
    /// <c>AUTOBOOT.DBP</c> into the save ZIP. dosbox_pure runs it (RUN_EXEC) with the disc still
    /// mounted as D:, skipping the start menu. <paramref name="dosExePath"/> is a DOS path like
    /// <c>C:\WAR2\WAR2.EXE</c>. Returns false if there's no save to write into.</summary>
    public static bool SetAutoBoot(string saveDir, string dosExePath)
    {
        var zip = FindSaveZip(saveDir);
        if (zip is null)
            return false;
        try
        {
            using var archive = ZipFile.Open(zip, ZipArchiveMode.Update);
            archive.GetEntry(AutoBootEntry)?.Delete();
            var entry = archive.CreateEntry(AutoBootEntry);
            using var stream = entry.Open();
            using var writer = new StreamWriter(stream);
            writer.Write(dosExePath.Trim() + "\r\n"); // dosbox_pure WriteAutoBoot() uses CRLF
            return true;
        }
        catch
        {
            return false;
        }
    }
}
