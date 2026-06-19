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
    }
}
