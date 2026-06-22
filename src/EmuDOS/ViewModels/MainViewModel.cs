using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using EmuDOS.Core.Model;
using EmuDOS.Services;

namespace EmuDOS.ViewModels;

/// <summary>The library: the imported games plus drop-to-import. Status is surfaced only
/// while importing/downloading or on a problem — otherwise the shelf has the whole window.</summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly AppServices _services;

    [ObservableProperty]
    private string _status = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _showStatus;

    [ObservableProperty]
    private bool _isEditMode;

    partial void OnIsEditModeChanged(bool value)
    {
        if (value)
            Report("Edit mode — drag boxes onto the shelves, then Ctrl+S to save the layout.", busy: false);
        else
            ClearStatus();
    }

    public MainViewModel(AppServices services)
    {
        _services = services;
        MigrateFlatMedia();
        LoadLibrary();
        StartAutoCloudSync();
        StartControllerMonitor();
    }

    private EmuDOS.Core.Input.ControllerMonitor? _controllers;

    // Announce controller connect/disconnect (with the SDL3 friendly name) in the bottom status bar.
    private void StartControllerMonitor()
    {
        _controllers = new EmuDOS.Core.Input.ControllerMonitor(_services.Paths.CoresDir);
        _controllers.Connected += name => Ui(() => Report($"{name} connected", busy: false));
        _controllers.Disconnected += name => Ui(() => Report($"{name} disconnected", busy: false));
        _controllers.Start();
    }

    // If connected to GitHub, sync saves in the background at launch — never on the UI thread.
    private void StartAutoCloudSync()
    {
        var s = _services.Settings;
        if (string.IsNullOrEmpty(s.GitHubToken))
            return;

        string token = s.GitHubToken, login = s.GitHubLogin, repo = s.GitHubRepo;
        var gameboxesDir = _services.Paths.GameboxesDir;
        var dbPath = Path.Combine(_services.Paths.DataRoot, "library.db");
        var key = string.IsNullOrEmpty(s.CloudEncryptionPassphrase)
            ? null : EmuDOS.Metadata.CloudCrypto.DeriveKey(s.CloudEncryptionPassphrase);
        var log = _services.CloudLog;
        var gh = new EmuDOS.Metadata.GitHubSyncService(log.Info);

        _ = Task.Run(async () =>
        {
            try
            {
                log.Info("Auto-sync at launch starting…");
                Ui(() => Report("Syncing saves…", busy: true));
                var r = await gh.SyncAsync(token, login, repo, gameboxesDir, dbPath, encKey: key);
                Ui(() => Report(r.Ok
                    ? $"Saves synced — {r.Uploaded} uploaded, {r.Downloaded} downloaded."
                    : $"Cloud sync failed: {r.Error}", busy: false));
            }
            catch (Exception ex)
            {
                log.Info($"Auto-sync error: {ex.Message}");
                Ui(() => Report($"Cloud sync failed: {ex.Message}", busy: false));
            }
        });
    }

    private static void Ui(Action action) =>
        System.Windows.Application.Current?.Dispatcher.Invoke(action);

    // One-time move of legacy flat screenshots/videos into each game's per-game media folders.
    private void MigrateFlatMedia()
    {
        try
        {
            var s = _services.Settings;
            var p = _services.Paths;
            var flatShots = string.IsNullOrWhiteSpace(s.ScreenshotFolder) ? p.ScreenshotsDir : s.ScreenshotFolder;
            var flatVids = string.IsNullOrWhiteSpace(s.VideoFolder) ? p.VideosDir : s.VideoFolder;
            var marker = Path.Combine(p.DataRoot, ".media-migrated");
            var games = _services.Library.GetGames().Select(g => (g.Title, g.GameboxPath));
            EmuDOS.Core.Library.MediaMigration.Run(marker, flatShots, flatVids, games);
        }
        catch { /* migration is best-effort; capture already writes per-game */ }
    }

    public ObservableCollection<GameTile> Games { get; } = [];

    public void LoadLibrary()
    {
        Games.Clear();
        var use3D = _services.Settings.Use3DBoxes;
        foreach (var game in _services.Library.GetGames())
        {
            var style = _services.Store.ReadState(game.GameboxPath).BoxStyle;
            Games.Add(new GameTile(game, style, use3D));
        }
    }

    /// <summary>Show a transient status (import/download/problem).</summary>
    public void Report(string message, bool busy)
    {
        Status = message;
        IsBusy = busy;
        ShowStatus = true;
    }

    /// <summary>Hide the status bar (idle).</summary>
    public void ClearStatus()
    {
        IsBusy = false;
        ShowStatus = false;
    }

    /// <summary>
    /// Handle a drop: MT-32 ROMs and SoundFonts are installed into the system folder
    /// (Boxer-style "just drop the BIOS in"); everything else is imported as a game.
    /// </summary>
    public async Task HandleDropAsync(IEnumerable<string> paths)
    {
        var installed = new List<string>();
        var toImport = new List<string>();

        foreach (var path in paths)
        {
            if (File.Exists(path) && Core.Audio.SystemFileInstaller.IsSystemFile(path))
            {
                Install(path, installed);
            }
            else if (Directory.Exists(path))
            {
                // Look inside the folder for ROMs/SoundFonts (e.g. a dropped "MT-32 ROMs" folder).
                // Only recognised ROMs (by size) install; if none, it's a normal game folder.
                int before = installed.Count;
                foreach (var file in SystemFilesIn(path))
                    Install(file, installed);

                if (installed.Count == before)
                    toImport.Add(path);
            }
            else
            {
                toImport.Add(path);
            }
        }

        if (installed.Count > 0)
        {
            var ready = _services.SystemFiles.HasMt32 ? " MT-32 is ready." : string.Empty;
            Report($"Installed {string.Join(", ", installed.Distinct())}.{ready}", busy: false);
            _services.SystemLog.Info($"Drop complete: {installed.Count} file(s). HasMt32={_services.SystemFiles.HasMt32}");
        }

        if (toImport.Count > 0)
            await ImportPathsAsync(toImport);
    }

    private void Install(string file, List<string> installed)
    {
        var description = _services.SystemFiles.Install(file);
        if (description is not null)
        {
            installed.Add(description);
            _services.SystemLog.Info($"Installed {description}  <-  {file}");
        }
        else
        {
            _services.SystemLog.Info($"Ignored (not a recognised ROM/SoundFont size): {file}");
        }
    }

    private static IEnumerable<string> SystemFilesIn(string directory)
    {
        IEnumerable<string> Find(string pattern)
        {
            try { return Directory.EnumerateFiles(directory, pattern, SearchOption.AllDirectories); }
            catch { return []; }
        }

        return Find("*.rom").Concat(Find("*.sf2"));
    }

    /// <summary>Import each dropped path and refresh the shelf. A bundle of disc images sharing a
    /// base name (e.g. "Game (Disc 1/2/3)") becomes one multi-disc game; everything else imports
    /// individually.</summary>
    public async Task ImportPathsAsync(IEnumerable<string> paths)
    {
        var all = paths.ToList();
        var discFiles = all.Where(p => File.Exists(p) && Core.Import.ImportPipeline.IsDiscFile(p)).ToHashSet();
        var singles = all.Where(p => !discFiles.Contains(p)).ToList();
        var discSets = new List<IReadOnlyList<string>>();
        foreach (var set in Core.Import.ImportPipeline.GroupDiscSets(discFiles))
        {
            if (set.Count >= 2) discSets.Add(set);
            else singles.Add(set[0]); // a lone disc imports the normal single-disc way
        }

        bool hadError = false;
        string? installHint = null;
        string? warning = null;

        foreach (var set in discSets)
        {
            Report($"Importing {set.Count}-disc game…", busy: true);
            var result = await _services.Import.ImportDiscSetAsync(set);
            if (result.Success && result.GameboxPath is not null)
            {
                _services.Library.UpsertFromGamebox(result.GameboxPath);
                installHint = $"Imported a {set.Count}-disc game — open it to install, swapping discs from the in-game menu.";
            }
            else
            {
                _services.SystemLog.Info($"Import failed (disc set, {set.Count} discs): {result.Error}");
                Report($"Couldn't import discs: {result.Error}", busy: false);
                hadError = true;
            }
        }

        foreach (var path in singles)
        {
            var name = Path.GetFileName(path.TrimEnd('\\', '/'));
            Report($"Importing {name}…", busy: true);
            var result = await _services.Import.ImportAsync(path);
            if (result.Success && result.GameboxPath is not null)
            {
                _services.Library.UpsertFromGamebox(result.GameboxPath);
                if (result.Warning is not null)
                    warning = result.Warning;
                else if (result.Classification == Core.Import.ImportClassification.NeedsInstall)
                    installHint = $"Imported {name} — open it to install (the disc is mounted as D:).";
            }
            else
            {
                _services.SystemLog.Info($"Import failed for '{name}' [{path}]: {result.Error}");
                Report($"Couldn't import {name}: {result.Error}", busy: false);
                hadError = true;
            }
        }

        LoadLibrary();
        if (hadError)
        {
            // Leave the error on screen — don't run the art sweep, which would overwrite it.
            IsBusy = false;
            return;
        }

        if (warning is not null)
            Report(warning, busy: false);
        else if (installHint is not null)
            Report(installHint, busy: false);
        else
            ClearStatus();

        await FetchMissingArtAsync();
    }

    public bool HasSelection => Games.Any(g => g.IsSelected);

    public void SelectAll()
    {
        foreach (var tile in Games)
            tile.IsSelected = true;
    }

    public void ClearSelection()
    {
        foreach (var tile in Games)
            tile.IsSelected = false;
    }

    /// <summary>
    /// Remove the given games from the library and delete their gameboxes. Box art is preserved
    /// in the art cache so re-importing the same game restores the cover without a download.
    /// </summary>
    public void DeleteGames(IEnumerable<GameTile> tiles)
    {
        foreach (var tile in tiles.ToList())
        {
            _services.ArtCache.Stash(tile.Title, tile.BoxFrontPath); // safety net
            _services.ArtCache.StashMetadata(tile.Title, Path.Combine(tile.Game.GameboxPath, "metadata.json"));
            try { _services.Library.Remove(tile.Id); }
            catch { /* keep going */ }
            try
            {
                if (Directory.Exists(tile.Game.GameboxPath))
                    Directory.Delete(tile.Game.GameboxPath, recursive: true);
            }
            catch { /* leave the folder if locked; index row is gone */ }
        }

        LoadLibrary();
    }

    /// <summary>Fetch box covers for any games missing one, updating each tile as it arrives.</summary>
    /// <summary>Set a game's box art from dropped image bytes (re-encoded to PNG).</summary>
    public void SetBoxArt(GameTile tile, byte[] imageBytes)
    {
        try
        {
            Directory.CreateDirectory(tile.MediaDir);
            File.WriteAllBytes(tile.BoxFrontPath, ToPng(imageBytes) ?? imageBytes);
            tile.LoadCover();
            _services.ArtCache.Stash(tile.Title, tile.BoxFrontPath);
            Report($"Box art set for {tile.Title}.", busy: false);
        }
        catch (Exception ex)
        {
            Report($"Couldn't set box art: {ex.Message}", busy: false);
        }
    }

    // Normalize whatever was dropped (JPG/GIF/BMP/…) to PNG so box-front.png is always a real PNG.
    private static byte[]? ToPng(byte[] bytes)
    {
        try
        {
            using var input = new MemoryStream(bytes);
            var decoder = System.Windows.Media.Imaging.BitmapDecoder.Create(
                input,
                System.Windows.Media.Imaging.BitmapCreateOptions.PreservePixelFormat,
                System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);

            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(decoder.Frames[0]));
            using var output = new MemoryStream();
            encoder.Save(output);
            return output.ToArray();
        }
        catch
        {
            return null; // unsupported format — fall back to the raw bytes
        }
    }

    /// <summary>Re-fetch box art for a single game (overwrites only on success).</summary>
    public async Task DownloadArtAsync(GameTile tile)
    {
        Report($"Fetching art for {tile.Title}…", busy: true);
        try
        {
            var path = await _services.Art.FetchBoxFrontAsync(tile.Title, tile.MediaDir);
            if (path is not null)
            {
                tile.LoadCover();
                _services.ArtCache.Stash(tile.Title, tile.BoxFrontPath);
                Report($"Art updated for {tile.Title}.", busy: false);
            }
            else
            {
                Report($"No art found for {tile.Title}.", busy: false);
            }
            await EnsureMetadataAsync(tile);
        }
        catch (Exception ex)
        {
            Report($"Art fetch failed: {ex.Message}", busy: false);
        }
    }

    // Ensure descriptive metadata exists: reuse the gamebox's, else the retained cache, else fetch
    // from ScreenScraper. Stored as the gamebox metadata.json (truth) + cached so it survives delete.
    // Off the UI thread; never touches UI.
    private async Task EnsureMetadataAsync(GameTile tile)
    {
        try
        {
            var root = tile.Game.GameboxPath;
            if (_services.Store.ReadMetadata(root) is not null)
                return;
            if (_services.ArtCache.TryRestoreMetadata(tile.Title, root))
                return;
            var md = await _services.Art.FetchMetadataAsync(tile.Title);
            if (md is not null && !md.IsEmpty)
            {
                _services.Store.WriteMetadata(root, md);
                _services.ArtCache.StashMetadata(tile.Title, Path.Combine(root, "metadata.json"));
            }
        }
        catch { /* metadata is a convenience; never block on it */ }
    }

    public async Task FetchMissingArtAsync()
    {
        var pending = Games.Where(t => t.Cover is null).ToList();
        if (pending.Count == 0)
        {
            Report("All games have art.", busy: false);
            return;
        }

        var total = pending.Count;
        var done = 0;
        await RunArtBatchAsync(pending, async tile =>
        {
            // On disk already, or restorable from the cache (e.g. a re-imported game) — no network.
            if (File.Exists(tile.BoxFrontPath) || _services.ArtCache.TryRestore(tile.Title, tile.MediaDir))
            {
                OnUI(tile.LoadCover);
                return;
            }

            var path = await _services.Art.FetchBoxFrontAsync(tile.Title, tile.MediaDir);
            if (path is not null)
            {
                _services.ArtCache.Stash(tile.Title, tile.BoxFrontPath);
                OnUI(tile.LoadCover);
            }
            await EnsureMetadataAsync(tile);
        }, () =>
        {
            var n = Interlocked.Increment(ref done);
            if (n % 5 == 0 || n == total)
                OnUI(() => Report($"Fetching box art… ({n} of {total})", busy: true));
        });

        ClearStatus();
    }

    /// <summary>Background backfill of descriptive metadata (genre/year/developer/description) for the
    /// whole library, so the game card is already populated when opened — the user never waits on it.
    /// Silent, off the UI thread, throttled to the ScreenScraper thread allowance. Per-game work is a
    /// no-op when metadata already exists (gamebox or cache), so it's cheap to run over everything.</summary>
    public Task FetchMissingMetadataAsync() =>
        RunArtBatchAsync(Games.ToList(), EnsureMetadataAsync, static () => { });

    /// <summary>Run an art-fetch action over many games concurrently, capped to the ScreenScraper
    /// account's allowed thread count (1 for free/anonymous — so it stays sequential there).</summary>
    private async Task RunArtBatchAsync(
        IReadOnlyList<GameTile> tiles, Func<GameTile, Task> fetch, Action onEach)
    {
        var threads = Math.Max(1, _services.Settings.ScreenScraperMaxThreads);
        using var sem = new SemaphoreSlim(threads, threads);
        var tasks = tiles.Select(async tile =>
        {
            await sem.WaitAsync();
            try { await fetch(tile); }
            catch { /* network/art hiccup — skip this one, keep going */ }
            finally { onEach(); sem.Release(); }
        });
        await Task.WhenAll(tasks);
    }

    // Marshal a UI-touching action (Cover/status changes) onto the dispatcher when called from a
    // background fetch task; runs inline when already on the UI thread.
    private static void OnUI(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
            action();
        else
            dispatcher.Invoke(action);
    }

    /// <summary>Download the 3D box render for a single game and switch that game to showing it.</summary>
    public async Task Download3DArtAsync(GameTile tile)
    {
        Report($"Fetching 3D box for {tile.Title}…", busy: true);
        try
        {
            var path = await _services.Art.FetchBox3DAsync(tile.Title, tile.MediaDir);
            if (path is not null)
            {
                // Downloading 3D = the user wants 3D for this game: persist the override and show it.
                SetGameBoxStyle(tile, BoxStyle.ThreeD);
                Report($"3D box downloaded for {tile.Title}.", busy: false);
            }
            else
            {
                Report($"No 3D box found for {tile.Title}.", busy: false);
            }
        }
        catch (Exception ex)
        {
            Report($"3D box fetch failed: {ex.Message}", busy: false);
        }
    }

    /// <summary>Download 3D box renders for every game that doesn't have one yet, switching the shelf
    /// to 3D so each box appears as it arrives (games without a 3D box fall back to their 2D cover).</summary>
    public async Task FetchAll3DArtAsync()
    {
        var pending = Games.Where(t => !t.Has3D).ToList();
        if (pending.Count == 0)
        {
            // Nothing to fetch, but still honour the intent: show 3D where we have it.
            SetGlobalBoxStyle(use3D: true);
            return;
        }

        // Flip to 3D up front so each downloaded box switches over live during the run.
        SetGlobalBoxStyle(use3D: true);

        var total = pending.Count;
        var got = 0;
        var done = 0;
        await RunArtBatchAsync(pending, async tile =>
        {
            var path = await _services.Art.FetchBox3DAsync(tile.Title, tile.MediaDir);
            if (path is not null)
            {
                Interlocked.Increment(ref got);
                OnUI(tile.LoadCover);
            }
        }, () =>
        {
            var n = Interlocked.Increment(ref done);
            if (n % 5 == 0 || n == total)
                OnUI(() => Report($"Fetching 3D boxes… ({n} of {total})", busy: true));
        });

        Report($"Downloaded {got} 3D box{(got == 1 ? "" : "es")}; showing 3D boxes.", busy: false);
    }

    /// <summary>Re-apply the current global box style to every tile (e.g. after Preferences changed it).
    /// Reads the saved setting; does not write or report.</summary>
    public void ReapplyBoxStyle()
    {
        var use3D = _services.Settings.Use3DBoxes;
        foreach (var tile in Games)
            tile.ApplyStyle(tile.StyleOverride, use3D);
    }

    /// <summary>Set the global default box style and re-apply it to every game on the shelf.</summary>
    public void SetGlobalBoxStyle(bool use3D)
    {
        _services.Settings.Use3DBoxes = use3D;
        _services.SettingsStore.Save(_services.Settings);
        foreach (var tile in Games)
            tile.ApplyStyle(tile.StyleOverride, use3D);
        Report($"Showing {(use3D ? "3D" : "2D")} boxes.", busy: false);
    }

    /// <summary>Override (or clear) the box style for a single game, persisted to its state.json.</summary>
    public void SetGameBoxStyle(GameTile tile, BoxStyle style)
    {
        var path = tile.Game.GameboxPath;
        var state = _services.Store.ReadState(path) with { BoxStyle = style };
        _services.Store.WriteState(path, state);
        tile.ApplyStyle(style, _services.Settings.Use3DBoxes);
    }
}
