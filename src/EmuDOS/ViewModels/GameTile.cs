using EmuDOS.Core.Library;

namespace EmuDOS.ViewModels;

/// <summary>A single game as shown on the shelf.</summary>
public sealed class GameTile(LibraryGame game)
{
    public long Id => game.Id;

    public string Title => game.Title;

    public LibraryGame Game => game;
}
