namespace EmuDOS.Core.Model;

/// <summary>A single mounted drive: a folder, or a CD/floppy/disk image.</summary>
public sealed record MountSpec
{
    public char DriveLetter { get; init; } = 'C';

    public MountKind Kind { get; init; } = MountKind.Hdd;

    /// <summary>
    /// Folder path, or path to a disk/CD image (ISO/CUE/IMG), relative to the gamebox root.
    /// </summary>
    public string Path { get; init; } = string.Empty;

    /// <summary>Optional volume label (some games check the CD label).</summary>
    public string? Label { get; init; }
}
