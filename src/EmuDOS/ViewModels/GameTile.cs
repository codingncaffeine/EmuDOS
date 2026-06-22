using System.IO;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using EmuDOS.Core.Library;
using EmuDOS.Core.Model;
using EmuDOS.Metadata;

namespace EmuDOS.ViewModels;

/// <summary>A single game on the shelf, with its box cover (once available).</summary>
public sealed partial class GameTile : ObservableObject
{
    private readonly LibraryGame _game;
    private bool _prefer3D;

    [ObservableProperty]
    private BitmapImage? _cover;

    [ObservableProperty]
    private bool _isSelected;

    /// <summary>Whether the game is favorited (drives the shelf heart badge); toggled from the card.</summary>
    [ObservableProperty]
    private bool _isFavorite;

    public GameTile(LibraryGame game, BoxStyle styleOverride = BoxStyle.Default, bool globalUse3D = false)
    {
        _game = game;
        _isFavorite = game.IsFavorite;
        StyleOverride = styleOverride;
        _prefer3D = ResolvePrefer3D(styleOverride, globalUse3D);
        LoadCover();
    }

    /// <summary>This game's per-game box-style override (Default = follow the global setting).</summary>
    public BoxStyle StyleOverride { get; private set; }

    private static bool ResolvePrefer3D(BoxStyle styleOverride, bool globalUse3D) => styleOverride switch
    {
        BoxStyle.TwoD => false,
        BoxStyle.ThreeD => true,
        _ => globalUse3D,
    };

    /// <summary>Re-resolve the effective style (after a global or per-game change) and reload the cover.</summary>
    public void ApplyStyle(BoxStyle styleOverride, bool globalUse3D)
    {
        StyleOverride = styleOverride;
        _prefer3D = ResolvePrefer3D(styleOverride, globalUse3D);
        LoadCover();
    }

    /// <summary>Whether a 3D box render has been downloaded for this game.</summary>
    public bool Has3D => File.Exists(Box3DPath);

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

    public string Box3DPath => Path.Combine(MediaDir, ArtService.Box3DFileName);

    /// <summary>(Re)load the cover from the gamebox media folder, honouring the effective box style.
    /// Falls back to the other style's file when the preferred one hasn't been downloaded.</summary>
    public void LoadCover()
    {
        var preferred = _prefer3D ? Box3DPath : BoxFrontPath;
        var fallback = _prefer3D ? BoxFrontPath : Box3DPath;
        var source = File.Exists(preferred) ? preferred
                   : File.Exists(fallback) ? fallback
                   : null;
        if (source is null)
        {
            Cover = null;
            return;
        }

        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad; // read fully now; don't lock the file
            // IgnoreImageCache forces a re-read from disk every load: WPF otherwise caches decoded
            // images by URI, so a replaced cover (dropped or chosen) would show the stale image.
            image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            image.UriSource = new Uri(source);
            image.EndInit();
            image.Freeze();
            Cover = image;
        }
        catch
        {
            Cover = null;
        }
    }

    // When the cover arrives, the box resizes to its real aspect.
    partial void OnCoverChanged(BitmapImage? value)
    {
        OnPropertyChanged(nameof(AspectRatio));
        OnPropertyChanged(nameof(BoxWidth));
    }
}
