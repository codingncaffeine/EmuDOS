namespace EmuDOS.Core.Downloads;

/// <summary>What kind of payload a download is, and how to install it.</summary>
public enum DownloadKind
{
    /// <summary>A plain file saved as-is.</summary>
    File,

    /// <summary>A libretro core shipped as a .zip; the contained .dll is extracted.</summary>
    ZippedCore,
}

/// <summary>Where an asset belongs once installed (drives its target directory).</summary>
public enum AssetCategory
{
    Core,
    SoundFont,
    Bios,

    /// <summary>
    /// The curated config database. Embedded baseline ships with the app; a newer
    /// downloaded copy here takes precedence, so improved per-game settings reach users
    /// without an app update.
    /// </summary>
    Catalog,

    /// <summary>A native helper DLL we build (e.g. the MT-32 shim), loaded via a resolver.</summary>
    Native,
}

/// <summary>A downloadable third-party asset (not bundled, for size/licensing reasons).</summary>
public sealed record DownloadAsset
{
    public required string Id { get; init; }

    public required string DisplayName { get; init; }

    /// <summary>One-line explanation shown in the Downloads tab.</summary>
    public string Description { get; init; } = string.Empty;

    public required string Url { get; init; }

    public required DownloadKind Kind { get; init; }

    /// <summary>Final filename on disk (and, for a zipped core, the entry to extract).</summary>
    public required string FileName { get; init; }

    public AssetCategory Category { get; init; } = AssetCategory.Core;

    /// <summary>Optional pinned SHA-256 (hex). When set, a mismatch fails the download.</summary>
    public string? Sha256 { get; init; }
}

/// <summary>Progress of an in-flight download.</summary>
public readonly record struct DownloadProgress(long BytesReceived, long? TotalBytes)
{
    /// <summary>0–1 when the total size is known, otherwise null (indeterminate).</summary>
    public double? Fraction =>
        TotalBytes is > 0 ? Math.Clamp((double)BytesReceived / TotalBytes.Value, 0, 1) : null;
}

/// <summary>Outcome of a completed download.</summary>
public sealed record DownloadResult
{
    public required bool Success { get; init; }

    public string? InstalledPath { get; init; }

    /// <summary>SHA-256 (hex) of the installed file — record it to detect later tampering/clobbering.</summary>
    public string? Sha256 { get; init; }

    public string? Error { get; init; }
}
