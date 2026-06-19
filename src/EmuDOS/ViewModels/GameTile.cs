using System.IO;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using EmuDOS.Core.Library;
using EmuDOS.Metadata;

namespace EmuDOS.ViewModels;

/// <summary>A single game on the shelf, with its box cover (once available).</summary>
public sealed partial class GameTile : ObservableObject
{
    private readonly LibraryGame _game;

    [ObservableProperty]
    private BitmapImage? _cover;

    [ObservableProperty]
    private bool _isSelected;

    public GameTile(LibraryGame game)
    {
        _game = game;
        LoadCover();
    }

    public long Id => _game.Id;

    public string Title => _game.Title;

    public LibraryGame Game => _game;

    /// <summary>Uniform box height — all boxes share this so their bottoms rest on one shelf line.</summary>
    public double BoxHeight => 132;

    /// <summary>Cover aspect (w/h); a sensible DOS portrait default until the cover loads.</summary>
    public double AspectRatio =>
        Cover is { PixelWidth: > 0, PixelHeight: > 0 } c ? (double)c.PixelWidth / c.PixelHeight : 0.66;

    /// <summary>Box width derived from the cover's true aspect, so the art fills it exactly.</summary>
    public double BoxWidth => BoxHeight * AspectRatio;

    // Manual placement (edit mode): absolute position in the shelf panel; null = auto-flow.
    public double? ManualLeft { get; set; }

    public double? ManualBottom { get; set; }

    public bool IsManuallyPlaced => ManualLeft.HasValue && ManualBottom.HasValue;

    public string MediaDir => Path.Combine(_game.GameboxPath, "media");

    public string BoxFrontPath => Path.Combine(MediaDir, ArtService.BoxFrontFileName);

    /// <summary>(Re)load the cover from the gamebox media folder, if present.</summary>
    public void LoadCover()
    {
        if (!File.Exists(BoxFrontPath))
        {
            Cover = null;
            return;
        }

        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad; // don't lock the file
        image.UriSource = new Uri(BoxFrontPath);
        image.EndInit();
        image.Freeze();
        Cover = image;
    }

    // When the cover arrives, the box resizes to its real aspect.
    partial void OnCoverChanged(BitmapImage? value)
    {
        OnPropertyChanged(nameof(AspectRatio));
        OnPropertyChanged(nameof(BoxWidth));
    }
}
