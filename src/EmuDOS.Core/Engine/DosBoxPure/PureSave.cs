using System.IO.Compression;
using EmuDOS.Core.Model;

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

    /// <summary>
    /// "Graduate" a CD game whose whole install ended up inside the writable C: overlay (<c>.pure.zip</c>)
    /// into a normal folder layout: extract the install into <c>content/</c> and switch the profile to
    /// <see cref="SourceMediaType.Folder"/>. Folder games write saves in place, so the game no longer
    /// has to rewrite the entire (often hundreds-of-MB) overlay zip on every tiny pilot/config save —
    /// which is what caused the periodic ~1s hitch and the cloud-sync bloat.
    /// <para>Only runs when the overlay is large (it actually holds the install, not just saves) and a
    /// run target is pinned (<c>AUTOBOOT.DBP</c>). Non-destructive: the old overlay is kept as
    /// <c>.pure.zip.bak</c>. Returns the updated profile, or null if not applicable.</para>
    /// </summary>
    public static GameProfile? GraduateInstalledGame(string gameboxRoot, GameProfile profile,
        long minOverlayBytes = 40L * 1024 * 1024)
    {
        var saveDir = Path.Combine(gameboxRoot, "saves");
        var zip = FindSaveZip(saveDir);
        if (zip is null || new FileInfo(zip).Length < minOverlayBytes)
            return null; // no overlay, or it's just small saves on a read-only disc — leave it alone

        string relExe;
        try
        {
            using var ar = ZipFile.OpenRead(zip);
            var auto = ar.GetEntry(AutoBootEntry);
            if (auto is null)
                return null; // not pinned to a program yet (still being installed)
            using var reader = new StreamReader(auto.Open());
            var target = reader.ReadToEnd().Trim().Replace('/', '\\');
            if (target.StartsWith("C:\\", StringComparison.OrdinalIgnoreCase))
                target = target[3..];
            relExe = target.TrimStart('\\');
            if (relExe.Length == 0)
                return null;
        }
        catch { return null; }

        var contentDir = Path.Combine(gameboxRoot, "content");
        try
        {
            Directory.CreateDirectory(contentDir);
            using (var ar = ZipFile.OpenRead(zip))
                foreach (var e in ar.Entries)
                {
                    if (e.FullName.Length == 0 || e.FullName.EndsWith("/"))
                        continue; // directory marker
                    if (string.Equals(e.FullName, AutoBootEntry, StringComparison.OrdinalIgnoreCase))
                        continue; // folder games launch via DOSBOX.BAT, not AUTOBOOT
                    var dest = Path.Combine(contentDir, e.FullName.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    e.ExtractToFile(dest, overwrite: true);
                }
            File.Move(zip, zip + ".bak", overwrite: true); // keep as a backup; folder mode ignores it
        }
        catch { return null; }

        // Keep PreCommands (e.g. IMGMOUNT D: the disc) so CD-audio games still see the CD as D:.
        return profile with
        {
            SourceMedia = SourceMediaType.Folder,
            Launch = profile.Launch with { Executable = relExe },
        };
    }

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
