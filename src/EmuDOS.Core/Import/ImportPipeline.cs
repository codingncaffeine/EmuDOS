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
    private static readonly string[] DiscImageExtensions = [".iso", ".cue", ".bin", ".chd"];
    private static readonly string[] ExecutableExtensions = [".exe", ".com", ".bat"];
    private static readonly string[] InstallerStems = ["install", "setup", "inst", "instalar"];

    public async Task<ImportResult> ImportAsync(
        string sourcePath,
        IProgress<ImportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        string? gameboxPath = null;
        try
        {
            var title = DeriveTitle(sourcePath);
            gameboxPath = AllocateGameboxPath(title);
            var box = new Gamebox(gameboxPath);
            Directory.CreateDirectory(box.ContentDir);

            SourceMediaType media;
            string? discMount = null;
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
            else if (IsDiscImage(sourcePath))
            {
                progress?.Report(new ImportProgress("Copying disc image", null));
                discMount = await Task.Run(() => CopyDiscImage(sourcePath, box.ContentDir), cancellationToken);
                media = SourceMediaType.Iso;
            }
            else
            {
                throw new NotSupportedException($"Unsupported source: {sourcePath}");
            }

            List<string> executables;
            ImportClassification classification;
            string? chosen;
            GameProfile profile;
            string? warning = null;

            if (discMount is not null)
            {
                // A CD image: mount it as D: and boot to DOS so the user can run its installer.
                executables = [];
                chosen = null;
                classification = ImportClassification.NeedsInstall;

                if (discMount.EndsWith(".iso", StringComparison.OrdinalIgnoreCase)
                    && !IsIso9660(Path.Combine(box.ContentDir, discMount)))
                {
                    warning = $"'{title}' is a non-ISO9660 disc image (e.g. UDF) — the DOS emulator "
                            + "can only read standard ISO9660 CDs, so this one won't mount.";
                }

                profile = new GameProfile
                {
                    Title = title,
                    SourceMedia = media,
                    // C: drive path + -t cdrom: dosbox resolves a bare relative path against the host
                    // CWD, but c:\ traverses the mounted content; -t cdrom registers MSCDEX (a data-only
                    // -t iso doesn't) and reads cue/bin correctly.
                    Launch = new LaunchSpec { PreCommands = [$"IMGMOUNT D: \"c:\\{discMount}\" -t cdrom"] },
                };
            }
            else
            {
                // Convert an Alcohol .mds/.mdf image to a .cue up front (the .mdf is a raw track) so
                // it's recognized as a disc by the checks below — dosbox can't read .mds/.mdf directly.
                EnsureMountableCue(box.ContentDir);
                executables = FindExecutables(box.ContentDir);

                // A pre-installed game records its auto-run target in AUTOBOOT.DBP (dosbox_pure's "set
                // auto start"). Honor it: mount the content as C: and run that program. Without this, a
                // game sitting next to its CD image — and nested deep enough that the exe scan misses it —
                // is mistaken for a raw disc and dropped at the core's start menu. The bundled-disc
                // staging further below then mounts its CD as D:.
                if (AutobootDbp.TryParseExecutable(box.ContentDir) is { } autoStart)
                {
                    classification = ImportClassification.ReadyToPlay;
                    chosen = autoStart;
                    profile = new GameProfile { Title = title, SourceMedia = media, Launch = new LaunchSpec { Executable = chosen } };
                }
                // A zip/folder that is essentially just a CD image (a disc image present, with no
                // loose game program to run) is a CD game to install. Treat it as one so it mounts
                // via dosbox_pure's native disc loader — which reads long, spaced, multi-track
                // cue/bin names host-side — and boots to its installer. The autoexec IMGMOUNT path
                // can't open such filenames from inside the C: mount.
                else if (executables.Count == 0 && FindMountableDisc(box.ContentDir) is not null)
                {
                    media = SourceMediaType.Iso;
                    classification = ImportClassification.NeedsInstall;
                    chosen = null;
                    profile = new GameProfile { Title = title, SourceMedia = media, Launch = new LaunchSpec() };
                }
                else
                {
                    (classification, chosen) = Classify(executables, title);
                    // Follow a hardcoded-path launcher .bat to the real exe (eXoDOS-style shims that
                    // assume a fixed install path which breaks once imported into a subfolder).
                    if (chosen is not null)
                        chosen = DosExecutables.ResolveBatRedirect(box.ContentDir, chosen);
                    profile = new GameProfile
                    {
                        Title = title,
                        SourceMedia = media,
                        Launch = new LaunchSpec { Executable = chosen },
                    };
                }
            }

            // Enrich with curated config if the catalog recognizes the content (not for raw discs).
            if (resolver is not null && discMount is null)
            {
                var contentFiles = Directory.EnumerateFiles(box.ContentDir, "*", SearchOption.AllDirectories)
                    .Select(Path.GetFileName)
                    .Where(n => !string.IsNullOrEmpty(n))!;
                profile = resolver.Resolve(profile, contentFiles!);
            }

            // eXoDOS games ship a DOSBOX.BAT that is the authoritative launch recipe (mount the disc,
            // cd into the install, run the game — frequently the game binary lives on the CD). When it
            // mounts a disc, reproduce it verbatim instead of the heuristic exe pick, which can land on
            // a setup utility (e.g. ASK.COM) and never start the game.
            if (discMount is null && DosBoxBatLaunch.TryParse(box.ContentDir) is { } exoLaunch)
            {
                profile = profile with { Launch = exoLaunch };
                chosen = null;
            }

            // A folder/zip game with its own files PLUS a bundled CD image (e.g. a cd\*.cue) needs
            // that disc mounted as D:, or it asks for "disk 1" / fails its CD check. Done after the
            // resolver, whose curated launch can drop pre-commands.
            // The disc is flattened to the content ROOT under a short, space-free name first: dosbox
            // can't IMGMOUNT an image that's deeply nested or has a long/spaced name inside the
            // union-mounted C:. We then mount via the C: drive path with -t cdrom (the only type that
            // registers MSCDEX and reads raw MODE1/2352 cue/bin, keeping CD audio).
            profile = EnsureBundledDiscMounted(profile, box.ContentDir);

            store.WriteProfile(gameboxPath, profile);

            return new ImportResult
            {
                Success = true,
                GameboxPath = gameboxPath,
                Classification = classification,
                Executables = executables,
                ChosenExecutable = chosen,
                Warning = warning,
            };
        }
        catch (Exception ex)
        {
            DeleteOrphanGamebox(gameboxPath); // don't leave a half-created folder (it bumps the next try to "(2)")
            return new ImportResult { Success = false, Error = ex.Message };
        }
    }

    // Remove a gamebox folder created for an import that then failed, so failed attempts don't pile
    // up empty folders that also bump later imports to "(2)", "(3)", …
    private static void DeleteOrphanGamebox(string? gameboxPath)
    {
        if (gameboxPath is null)
            return;
        try
        {
            if (Directory.Exists(gameboxPath))
                Directory.Delete(gameboxPath, recursive: true);
        }
        catch { /* locked — leave it; better than throwing while already handling a failure */ }
    }

    private static bool IsArchive(string path) =>
        ArchiveExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    private static bool IsDiscImage(string path) =>
        DiscImageExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    // A raw ISO has its volume descriptors at sector 16+ (2048 bytes each), each tagged "CD001".
    // No CD001 means a non-ISO9660 image (e.g. UDF) that dosbox can't read. Unknown -> assume OK.
    public static bool IsIso9660(string isoPath)
    {
        try
        {
            using var stream = File.OpenRead(isoPath);
            var buffer = new byte[6];
            for (long sector = 16; sector <= 32; sector++)
            {
                stream.Seek(sector * 2048, SeekOrigin.Begin);
                if (stream.Read(buffer, 0, 6) < 6)
                    break;
                if (buffer[1] == 'C' && buffer[2] == 'D' && buffer[3] == '0' && buffer[4] == '0' && buffer[5] == '1')
                    return true;
            }
            return false;
        }
        catch
        {
            return true; // can't read it for some reason — don't cry wolf
        }
    }

    // Copy a disc image into the content folder under a short, space-free 8.3-friendly name —
    // DOS/dosbox can't open an image with a long, spaced filename. Returns the name to IMGMOUNT.
    // keepName: keep the source's (sanitized) filename instead of the short "disc.ext". Used for
    // multi-disc sets so each disc stays distinguishable in dosbox_pure's disc-swap menu.
    private static string CopyDiscImage(string sourcePath, string contentDir, bool keepName = false)
    {
        Directory.CreateDirectory(contentDir);
        var ext = Path.GetExtension(sourcePath).ToLowerInvariant();

        // For a .cue, names must match its FILE references, so keep them but only safe if short.
        if (ext == ".cue")
        {
            var name = Path.GetFileName(sourcePath);
            File.Copy(sourcePath, Path.Combine(contentDir, name), overwrite: true);
            var srcDir = Path.GetDirectoryName(sourcePath) ?? string.Empty;
            foreach (var track in CueReferencedFiles(sourcePath))
            {
                var src = Path.Combine(srcDir, track);
                if (File.Exists(src))
                    File.Copy(src, Path.Combine(contentDir, Path.GetFileName(track)), overwrite: true);
            }
            return name;
        }

        var dest = keepName ? SafeFileName(Path.GetFileName(sourcePath)) : "disc" + ext;
        File.Copy(sourcePath, Path.Combine(contentDir, dest), overwrite: true);
        return dest;
    }

    private static string SafeFileName(string name) =>
        string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));

    private static readonly System.Text.RegularExpressions.Regex DiscMarker = new(
        @"[\s_\-]*[\(\[]?\s*(disc|disk|cd|dvd)\s*\d+\s*[\)\]]?\s*$",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>Whether a path is a disc image we know how to mount.</summary>
    public static bool IsDiscFile(string path) =>
        DiscImageExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    /// <summary>A title with a trailing disc marker ("(Disc 2)", "CD1", "- Disk 3") removed.</summary>
    public static string StripDiscMarker(string name) =>
        string.IsNullOrEmpty(name) ? string.Empty : DiscMarker.Replace(name, string.Empty).Trim();

    /// <summary>Group disc-image paths into per-game sets by their disc-marker-stripped name, so a
    /// bundle like "Game (Disc 1).iso" + "Game (Disc 2).iso" lands as one multi-disc game.</summary>
    public static IEnumerable<IReadOnlyList<string>> GroupDiscSets(IEnumerable<string> discPaths) =>
        discPaths.GroupBy(p => StripDiscMarker(Path.GetFileNameWithoutExtension(p)), StringComparer.OrdinalIgnoreCase)
                 .Select(g => (IReadOnlyList<string>)g.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList());

    /// <summary>Import several disc images of one game as a single multi-disc gamebox.</summary>
    public async Task<ImportResult> ImportDiscSetAsync(
        IReadOnlyList<string> discPaths,
        IProgress<ImportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(discPaths);
        if (discPaths.Count == 0)
            return new ImportResult { Success = false, Error = "No discs to import." };
        string? gameboxPath = null;
        try
        {
            var title = StripDiscMarker(Path.GetFileNameWithoutExtension(discPaths[0]));
            if (string.IsNullOrWhiteSpace(title))
                title = "Untitled";

            gameboxPath = AllocateGameboxPath(title);
            var box = new Gamebox(gameboxPath);
            Directory.CreateDirectory(box.ContentDir);

            foreach (var disc in discPaths)
            {
                progress?.Report(new ImportProgress($"Copying {Path.GetFileName(disc)}", null));
                await Task.Run(() => CopyDiscImage(disc, box.ContentDir, keepName: true), cancellationToken);
            }

            var profile = new GameProfile { Title = title, SourceMedia = SourceMediaType.Iso, Launch = new LaunchSpec() };
            store.WriteProfile(gameboxPath, profile);

            return new ImportResult
            {
                Success = true,
                GameboxPath = gameboxPath,
                Classification = ImportClassification.NeedsInstall,
            };
        }
        catch (Exception ex)
        {
            DeleteOrphanGamebox(gameboxPath); // don't leave a half-created folder behind on failure
            return new ImportResult { Success = false, Error = ex.Message };
        }
    }

    public static IEnumerable<string> CueReferencedFiles(string cuePath)
    {
        foreach (var line in File.ReadLines(cuePath))
        {
            var trimmed = line.TrimStart();
            if (!trimmed.StartsWith("FILE", StringComparison.OrdinalIgnoreCase))
                continue;

            int open = trimmed.IndexOf('"');
            int close = open >= 0 ? trimmed.IndexOf('"', open + 1) : -1;
            if (open >= 0 && close > open)
                yield return trimmed.Substring(open + 1, close - open - 1);
        }
    }

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

        // Skip past folders that are REAL gameboxes (they have a profile.json) so we never clobber an
        // existing game's content/saves. We stop at the first name that's free OR that's an orphan
        // (a folder left by an earlier failed import) — which we reuse instead of spawning a "(2)".
        var candidate = Path.Combine(paths.GameboxesDir, safe);
        var n = 2;
        while (Directory.Exists(candidate) && new Gamebox(candidate).Exists)
            candidate = Path.Combine(paths.GameboxesDir, $"{safe} ({n++})");

        if (Directory.Exists(candidate))
            DeleteOrphanGamebox(candidate); // reuse the orphan's name; clear its stale partial content
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

    // A CD image bundled inside a game folder (commonly a cd\*.cue), to mount as D:. Prefers a .cue
    // (carries CD audio), then .iso, then .chd. Relative DOS path; null if the folder ships no disc.
    private static string? FindMountableDisc(string contentDir) =>
        Directory.EnumerateFiles(contentDir, "*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".cue", StringComparison.OrdinalIgnoreCase)
                     || f.EndsWith(".iso", StringComparison.OrdinalIgnoreCase)
                     || f.EndsWith(".chd", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f.EndsWith(".cue", StringComparison.OrdinalIgnoreCase) ? 0
                        : f.EndsWith(".iso", StringComparison.OrdinalIgnoreCase) ? 1 : 2)
            .ThenBy(f => f, StringComparer.OrdinalIgnoreCase)
            .Select(f => Path.GetRelativePath(contentDir, f).Replace('/', '\\'))
            .FirstOrDefault();

    // Write a .cue for any Alcohol .mdf image (a raw MODE1/2352 track) so it can be mounted —
    // neither EmuDOS nor dosbox reads .mds/.mdf directly. The cue sits next to the .mdf.
    private static void EnsureMountableCue(string contentDir)
    {
        foreach (var mdf in Directory.EnumerateFiles(contentDir, "*.mdf", SearchOption.AllDirectories).ToList())
        {
            var cue = Path.ChangeExtension(mdf, ".cue");
            if (File.Exists(cue))
                continue;
            try
            {
                File.WriteAllText(cue,
                    $"FILE \"{Path.GetFileName(mdf)}\" BINARY\r\n  TRACK 01 MODE1/2352\r\n    INDEX 01 00:00:00\r\n");
            }
            catch { /* leave it — just won't mount */ }
        }
    }

    private static readonly string[] RootableDiscExts = [".cue", ".iso", ".chd"];

    /// <summary>Flatten a folder game's bundled CD image to the content ROOT under a short, space-free
    /// name and return the root mount target (e.g. "gamecd.cue"). dosbox can't IMGMOUNT an image that's
    /// deeply nested or has a long/spaced name inside the union-mounted C:. For a .cue, its data tracks
    /// are moved alongside and the cue's FILE lines rewritten to match. Null if there's no disc.</summary>
    /// <summary>
    /// Flatten a folder game's bundled CD(s) to the content ROOT under short, space-free names and
    /// return them in disc order, so a multi-disc game ends up as one swappable D: set. dosbox can't
    /// IMGMOUNT an image that's deeply nested or has a long/spaced name from inside the union-mounted
    /// C:, so each disc — and, for a .cue, its referenced tracks — is copied to the root and the cue
    /// rewritten to the new names. Returns an empty list when there's nothing to stage.
    /// </summary>
    /// <summary>
    /// Ensure a folder/zip game with a bundled CD image mounts it as the D: CD-ROM. Folders (unlike the
    /// Iso m3u8 path) don't auto-mount a disc inside, so we stage every disc of the set to the content
    /// root under short, space-free names and IMGMOUNT them as one swappable D: drive (with -t cdrom, so
    /// MSCDEX + CD audio work). No-op for Iso games (they mount via the m3u8) or when a D: mount is
    /// already set. Shared by import, the post-install graduation, and launch so every game — existing
    /// or freshly imported — gets the disc mounted the same way.
    /// </summary>
    public static GameProfile EnsureBundledDiscMounted(GameProfile profile, string contentDir)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.SourceMedia == SourceMediaType.Iso)
            return profile;
        if (profile.Launch.PreCommands.Any(c => c.Contains("IMGMOUNT D:", StringComparison.OrdinalIgnoreCase)))
            return profile;
        if (StageBundledDiscsAtRoot(contentDir) is not { Count: > 0 } rootDiscs)
            return profile;

        // One D: drive holds the whole set so a multi-disc game can swap discs via dosbox_pure's menu.
        var images = string.Join(" ", rootDiscs.Select(d => $"\"c:\\{d}\""));
        var pre = new List<string> { $"IMGMOUNT D: {images} -t cdrom" };
        pre.AddRange(profile.Launch.PreCommands);
        return profile with { Launch = profile.Launch with { PreCommands = pre } };
    }

    private static IReadOnlyList<string> StageBundledDiscsAtRoot(string contentDir)
    {
        var all = Directory.EnumerateFiles(contentDir, "*", SearchOption.AllDirectories)
            .Where(f => RootableDiscExts.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .ToList();
        if (all.Count == 0)
            return [];

        // Discs of one game ("…DISC1.cue" + "…DISC2.cue") group into a single swap set; prefer the
        // largest set (a real multi-disc game) over a stray loose image.
        var set = GroupDiscSets(all)
            .OrderByDescending(s => s.Count)
            .ThenBy(s => s[0], StringComparer.OrdinalIgnoreCase)
            .First();

        var staged = new List<string>();
        var disc = 0;
        foreach (var image in set)
        {
            disc++;
            var stem = set.Count == 1 ? "gamecd" : $"gamecd{disc:00}";
            if (StageOneDisc(image, contentDir, stem) is { } name)
                staged.Add(name);
        }
        return staged;
    }

    // Copy one disc image (and, for a .cue, its referenced tracks) to the content root as
    // <stem>.<ext>, rewriting the cue's FILE references; returns the root image name, or null on failure.
    private static string? StageOneDisc(string image, string contentDir, string stem)
    {
        try
        {
            var ext = Path.GetExtension(image).ToLowerInvariant();
            if (ext != ".cue")
                return MoveToRoot(image, contentDir, stem + ext);

            var cueDir = Path.GetDirectoryName(image)!;
            var cueText = File.ReadAllText(image);
            var bins = CueReferencedFiles(image).ToList();
            var track = 0;
            foreach (var bin in bins)
            {
                var binName = bins.Count == 1
                    ? stem + Path.GetExtension(bin)
                    : $"{stem}_{++track:00}" + Path.GetExtension(bin);
                var src = Path.Combine(cueDir, bin);
                if (File.Exists(src))
                    MoveToRoot(src, contentDir, binName);
                cueText = cueText.Replace('"' + bin + '"', '"' + binName + '"');
            }
            var rootCue = Path.Combine(contentDir, stem + ".cue");
            File.WriteAllText(rootCue, cueText);
            if (!string.Equals(Path.GetFullPath(image), rootCue, StringComparison.OrdinalIgnoreCase))
                File.Delete(image);
            return stem + ".cue";
        }
        catch
        {
            return null; // staging this disc failed — skip it; the others may still mount
        }
    }

    private static string MoveToRoot(string src, string contentDir, string destName)
    {
        var dest = Path.Combine(contentDir, destName);
        if (!string.Equals(Path.GetFullPath(src), Path.GetFullPath(dest), StringComparison.OrdinalIgnoreCase))
        {
            if (File.Exists(dest))
                File.Delete(dest);
            File.Move(src, dest);
        }
        return destName;
    }

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
        var titled = candidates.FirstOrDefault(c => DosExecutables.TitleMatches(c, title));
        if (titled is not null)
            return titled;

        // A canonical launcher (SIERRA.EXE, RUN.BAT, …) beats the generic guesses below — Sierra
        // games have no title-named exe and would otherwise fall to the largest/first executable.
        var known = candidates.FirstOrDefault(DosExecutables.IsKnownLauncher);
        if (known is not null)
            return known;

        // Extender-based game (e.g. DOS/4GW): the launcher batch is the right target, not the raw exe.
        if (preferBat
            && candidates.FirstOrDefault(c => c.EndsWith(".bat", StringComparison.OrdinalIgnoreCase)) is { } bat)
            return bat;

        // Prefer a real .exe over a bundled utility (patcher, prep wizard, …) when guessing.
        var exes = candidates.Where(c => c.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)).ToList();
        return exes.FirstOrDefault(c => !DosExecutables.IsLikelyUtility(c))
            ?? exes.FirstOrDefault()
            ?? candidates[0];
    }
}
