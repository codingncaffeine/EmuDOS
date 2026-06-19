using EmuDOS.Core.Infrastructure;

namespace EmuDOS.Core.Audio;

/// <summary>
/// Installs dropped MT-32/CM-32L ROMs and SoundFonts into the engine system directory — the
/// Boxer-style "just drop the BIOS in" flow. ROMs are recognised by size and copied under
/// canonical names so both the core and our own synth can find them.
/// </summary>
public sealed class SystemFileInstaller
{
    public const string Mt32Control = "MT32_CONTROL.ROM";
    public const string Mt32Pcm = "MT32_PCM.ROM";
    public const string Cm32lPcm = "CM32L_PCM.ROM";

    private readonly string _systemDir;

    public SystemFileInstaller(AppPaths paths) => _systemDir = paths.SystemDir;

    public string SystemDir => _systemDir;

    /// <summary>A file we install into the system dir rather than importing as a game.</summary>
    public static bool IsSystemFile(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".rom" or ".sf2";
    }

    /// <summary>Copy a dropped ROM/SoundFont into the system dir. Returns a description, or null if unrecognised.</summary>
    public string? Install(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext == ".sf2")
        {
            Copy(path, Path.GetFileName(path));
            return $"SoundFont \"{Path.GetFileName(path)}\"";
        }

        if (ext == ".rom")
        {
            var (name, label) = ClassifyRom(path);
            if (name is null)
                return null;
            Copy(path, name);
            return label;
        }

        return null;
    }

    /// <summary>True once a usable MT-32 (or CM-32L) ROM pair is present.</summary>
    public bool HasMt32 =>
        File.Exists(Path.Combine(_systemDir, Mt32Control))
        && (File.Exists(Path.Combine(_systemDir, Mt32Pcm)) || File.Exists(Path.Combine(_systemDir, Cm32lPcm)));

    private static (string? Name, string? Label) ClassifyRom(string path)
    {
        long length;
        try { length = new FileInfo(path).Length; }
        catch { return (null, null); }

        // Control ROM is 64KB for both MT-32 and CM-32L (indistinguishable by size); PCM differs.
        return length switch
        {
            65536 => (Mt32Control, "MT-32 control ROM"),
            524288 => (Mt32Pcm, "MT-32 PCM ROM"),
            1048576 => (Cm32lPcm, "CM-32L PCM ROM"),
            _ => (null, null),
        };
    }

    private void Copy(string source, string name)
    {
        Directory.CreateDirectory(_systemDir);
        File.Copy(source, Path.Combine(_systemDir, name), overwrite: true);
    }
}
