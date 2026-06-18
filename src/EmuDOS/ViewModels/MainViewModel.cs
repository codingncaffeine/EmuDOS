using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using EmuDOS.Services;

namespace EmuDOS.ViewModels;

/// <summary>The shelf: the imported library plus drop-to-import.</summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly AppServices _services;

    [ObservableProperty]
    private string _status = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

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

        Status = Games.Count == 0
            ? "Drop a DOS game folder or archive here to begin."
            : $"{Games.Count} game{(Games.Count == 1 ? "" : "s")} on the shelf.";
    }

    /// <summary>Import each dropped path (folder/archive) and refresh the shelf.</summary>
    public async Task ImportPathsAsync(IEnumerable<string> paths)
    {
        IsBusy = true;
        try
        {
            foreach (var path in paths)
            {
                Status = $"Importing {Path.GetFileName(path.TrimEnd('\\', '/'))}…";
                var result = await _services.Import.ImportAsync(path);
                if (result.Success && result.GameboxPath is not null)
                    _services.Library.UpsertFromGamebox(result.GameboxPath);
                else
                    Status = $"Couldn't import {Path.GetFileName(path)}: {result.Error}";
            }

            LoadLibrary();
        }
        finally
        {
            IsBusy = false;
        }
    }
}
