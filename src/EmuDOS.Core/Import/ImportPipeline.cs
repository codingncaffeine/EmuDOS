using EmuDOS.Core.Catalog;
using EmuDOS.Core.Infrastructure;
using EmuDOS.Core.Library;
using EmuDOS.Core.Model;
using SharpCompress.Archives;
using SharpCompress.Common;

namespace EmuDOS.Core.Import;

/// <summary>
/// Imports a folder or archive (.zip/.rar/.7z) into a gamebox: extracts/copies the content,
/// finds executables, classifies the result, and writes a profile.json. When a
/// <see cref="ProfileResolver"/> is supplied, a recognized game is enriched with its curated
/// config on the way in.
/// </summary>
public sealed class ImportPipeline(AppPaths paths, GameboxStore store, ProfileResolver? resolver = null)
    : IImportPipeline
{
    private static readonly string[] ArchiveExtensions = [".zip", ".rar", ".7z"];
    private static readonly string[] ExecutableExtensions = [".exe", ".com", ".bat"];
    private static readonly string[] InstallerStems = ["install", "setup", "inst", "instalar"];

    public async Task<ImportResult> ImportAsync(
        string sourcePath,
        IProgress<ImportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        try
        {
            var title = DeriveTitle(sourcePath);
            var gameboxPath = AllocateGameboxPath(title);
            var box = new Gamebox(gameboxPath);
            Directory.CreateDirectory(box.ContentDir);

            SourceMediaType media;
            if (Directory.Exists(sourcePath))
            {
                progress?.Report(new ImportProgress("Copying", null));
                await Task.Run(() => CopyDirectory(sourcePath, box.ContentDir), cancellationToken);
                media = SourceMediaType.Folder;
            }
            else if (IsArchive(sourcePath))
            {
                progress?.Report(new ImportProgress("Extracting", null));
                await Task.Run(() => ExtractArchive(sourcePath, box.ContentDir, cancellationToken), cancellationToken);
                media = SourceMediaType.Zip;
            }
            else
            {
                throw new NotSupportedException($"Unsupported source: {sourcePath}");
            }

            var executables = FindExecutables(box.ContentDir);
            var (classification, chosen) = Classify(executables, title);

            var profile = new GameProfile
            {
                Title = title,
                SourceMedia = media,
                Launch = new LaunchSpec { Executable = chosen },
            };

            // Enrich with curated config if the catalog recognizes the content.
            if (resolver is not null)
            {
                var contentFiles = Directory.EnumerateFiles(box.ContentDir, "*", SearchOption.AllDirectories)
                    .Select(Path.GetFileName)
                    .Where(n => !string.IsNullOrEmpty(n))!;
                profile = resolver.Resolve(profile, contentFiles!);
            }

            store.WriteProfile(gameboxPath, profile);

            return new ImportResult
            {
                Success = true,
                GameboxPath = gameboxPath,
                Classification = classification,
                Executables = executables,
                ChosenExecutable = chosen,
            };
        }
        catch (Exception ex)
        {
            return new ImportResult { Success = false, Error = ex.Message };
        }
    }

    private static bool IsArchive(string path) =>
        ArchiveExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    private static string DeriveTitle(string sourcePath)
    {
        var name = Directory.Exists(sourcePath)
            ? new DirectoryInfo(sourcePath).Name
            : Path.GetFileNameWithoutExtension(sourcePath);
        return string.IsNullOrWhiteSpace(name) ? "Untitled" : name;
    }

    private string AllocateGameboxPath(string title)
    {
        var safe = string.Concat(title.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim();
        if (safe.Length == 0)
            safe = "game";

        var candidate = Path.Combine(paths.GameboxesDir, safe);
        var n = 2;
        while (Directory.Exists(candidate))
            candidate = Path.Combine(paths.GameboxesDir, $"{safe} ({n++})");
        return candidate;
    }

    private static void ExtractArchive(string archivePath, string destination, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        // ArchiveFactory auto-detects zip/rar/7z and extracts the whole archive.
        ArchiveFactory.WriteToDirectory(archivePath, destination,
            new ExtractionOptions { ExtractFullPath = true, Overwrite = true });
    }

    private static void CopyDirectory(string source, string destination)
    {
        foreach (var dir in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, dir)));

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(destination, Path.GetRelativePath(source, file)), overwrite: true);
    }

    private static List<string> FindExecutables(string contentDir) =>
        Directory.EnumerateFiles(contentDir, "*", SearchOption.AllDirectories)
            .Where(f => ExecutableExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .Select(f => Path.GetRelativePath(contentDir, f).Replace('/', '\\'))
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static (ImportClassification, string?) Classify(List<string> executables, string title)
    {
        // A DOS extender means the real launcher is almost always a .bat that invokes it.
        bool hasExtender = executables.Any(DosExecutables.IsExtender);
        var launchable = executables.Where(e => !DosExecutables.IsRuntimeHelper(e)).ToList();

        var games = launchable.Where(e => !IsInstaller(e)).ToList();
        if (games.Count > 0)
            return (ImportClassification.ReadyToPlay, PickBest(games, title, hasExtender));

        var installers = launchable.Where(IsInstaller).ToList();
        if (installers.Count > 0)
            return (ImportClassification.NeedsInstall, PickBest(installers, title, hasExtender));

        return (ImportClassification.Unknown, launchable.FirstOrDefault() ?? executables.FirstOrDefault());
    }

    private static bool IsInstaller(string relativePath) =>
        InstallerStems.Contains(Path.GetFileNameWithoutExtension(relativePath).ToLowerInvariant());

    private static string PickBest(List<string> candidates, string title, bool preferBat)
    {
        var titleTokens = title.Split([' ', '_', '-', '.'], StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.ToLowerInvariant())
            .ToHashSet();

        var titled = candidates.FirstOrDefault(c =>
            titleTokens.Contains(Path.GetFileNameWithoutExtension(c).ToLowerInvariant()));
        if (titled is not null)
            return titled;

        // Extender-based game (e.g. DOS/4GW): the launcher batch is the right target, not the raw exe.
        if (preferBat
            && candidates.FirstOrDefault(c => c.EndsWith(".bat", StringComparison.OrdinalIgnoreCase)) is { } bat)
            return bat;

        return candidates.FirstOrDefault(c => c.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            ?? candidates[0];
    }
}
