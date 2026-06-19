using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
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
        LoadLibrary();
    }

    public ObservableCollection<GameTile> Games { get; } = [];

    public void LoadLibrary()
    {
        Games.Clear();
        foreach (var game in _services.Library.GetGames())
            Games.Add(new GameTile(game));
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

    /// <summary>Import each dropped path (folder/archive) and refresh the shelf.</summary>
    public async Task ImportPathsAsync(IEnumerable<string> paths)
    {
        bool hadError = false;
        foreach (var path in paths)
        {
            var name = Path.GetFileName(path.TrimEnd('\\', '/'));
            Report($"Importing {name}…", busy: true);
            var result = await _services.Import.ImportAsync(path);
            if (result.Success && result.GameboxPath is not null)
            {
                _services.Library.UpsertFromGamebox(result.GameboxPath);
            }
            else
            {
                Report($"Couldn't import {name}: {result.Error}", busy: false);
                hadError = true;
            }
        }

        LoadLibrary();
        if (!hadError)
            ClearStatus();
        else
            IsBusy = false;

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
        }
        catch (Exception ex)
        {
            Report($"Art fetch failed: {ex.Message}", busy: false);
        }
    }

    public async Task FetchMissingArtAsync()
    {
        var pending = Games.Where(t => t.Cover is null).ToList();
        if (pending.Count == 0)
        {
            Report("All games have art.", busy: false);
            return;
        }

        foreach (var tile in pending)
        {
            if (File.Exists(tile.BoxFrontPath))
            {
                tile.LoadCover();
                continue;
            }

            // Restore from the art cache first (e.g. re-import of a previously-deleted game) —
            // avoids a needless re-download.
            if (_services.ArtCache.TryRestore(tile.Title, tile.MediaDir))
            {
                tile.LoadCover();
                continue;
            }

            Report($"Fetching art for {tile.Title}…", busy: true);
            try
            {
                var path = await _services.Art.FetchBoxFrontAsync(tile.Title, tile.MediaDir);
                if (path is not null)
                {
                    tile.LoadCover();
                    _services.ArtCache.Stash(tile.Title, tile.BoxFrontPath);
                }
            }
            catch
            {
                // Network/art hiccup — skip this one, keep going.
            }
        }

        ClearStatus();
    }
}
