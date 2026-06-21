using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace EmuDOS.Core.Import;

/// <summary>
/// Builds a standard ISO9660 + Joliet CD image from a folder, using Windows' built-in Image
/// Mastering API (IMAPI2). Turns loose game files — or a folder a user extracted from a rip the
/// emulator can't read (e.g. UDF) — into a disc that dosbox and a guest OS can mount.
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public static class IsoBuilder
{
    private const int FsiFileSystemISO9660 = 1;
    private const int FsiFileSystemJoliet = 2;
    private const int StatflagNoName = 1;

    /// <summary>Build <paramref name="isoPath"/> from the contents of <paramref name="folderPath"/>
    /// (the folder's files land at the disc root). Synchronous and COM-heavy — run it on a
    /// background STA thread, not the UI thread.</summary>
    public static void BuildFromFolder(string folderPath, string isoPath, string volumeLabel)
    {
        if (!Directory.Exists(folderPath))
            throw new DirectoryNotFoundException($"Folder not found: {folderPath}");

        var imageType = Type.GetTypeFromProgID("IMAPI2FS.MsftFileSystemImage")
            ?? throw new NotSupportedException("Windows' disc imaging API (IMAPI2) isn't available on this system.");

        dynamic fsi = Activator.CreateInstance(imageType)
            ?? throw new NotSupportedException("Couldn't start the disc imaging API.");
        try
        {
            fsi.FileSystemsToCreate = FsiFileSystemISO9660 | FsiFileSystemJoliet;
            fsi.VolumeName = SanitizeLabel(volumeLabel);
            fsi.FreeMediaBlocks = FolderSizeBytes(folderPath) / 2048 + 50_000; // data blocks + slack
            fsi.Root.AddTree(folderPath, false); // false = put the folder's contents at the disc root

            dynamic result = fsi.CreateResultImage();
            WriteStreamToFile((IStream)result.ImageStream, isoPath);
        }
        finally
        {
            Marshal.FinalReleaseComObject(fsi);
        }
    }

    private static long FolderSizeBytes(string folder) =>
        new DirectoryInfo(folder).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);

    // ISO9660 volume identifier: up to 32 chars, A-Z / 0-9 / underscore.
    private static string SanitizeLabel(string label)
    {
        var cleaned = new string(label.ToUpperInvariant()
            .Select(c => (char.IsLetterOrDigit(c) && c < 128) || c == '_' ? c : '_')
            .ToArray()).Trim('_');
        if (cleaned.Length > 32)
            cleaned = cleaned[..32];
        return cleaned.Length == 0 ? "DISC" : cleaned;
    }

    private static void WriteStreamToFile(IStream stream, string isoPath)
    {
        stream.Stat(out var stat, StatflagNoName);
        long remaining = stat.cbSize;
        using var file = File.Create(isoPath);
        var buffer = new byte[1 << 20];
        var read = Marshal.AllocHGlobal(sizeof(int));
        try
        {
            while (remaining > 0)
            {
                int want = (int)Math.Min(buffer.Length, remaining);
                stream.Read(buffer, want, read);
                int got = Marshal.ReadInt32(read);
                if (got <= 0)
                    break;
                file.Write(buffer, 0, got);
                remaining -= got;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(read);
        }
    }
}
