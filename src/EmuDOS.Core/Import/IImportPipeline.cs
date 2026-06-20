namespace EmuDOS.Core.Import;

/// <summary>
/// Turns whatever the user drops — a folder, .zip, .rar, or .7z — into a gamebox. We never
/// limit the input format; everything converges on a gamebox (the source of truth).
/// </summary>
public interface IImportPipeline
{
    Task<ImportResult> ImportAsync(
        string sourcePath,
        IProgress<ImportProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>Import several disc images of one game as a single multi-disc gamebox.</summary>
    Task<ImportResult> ImportDiscSetAsync(
        IReadOnlyList<string> discPaths,
        IProgress<ImportProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
