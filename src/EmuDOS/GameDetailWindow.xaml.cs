using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using EmuDOS.Core.Library;
using EmuDOS.Core.Model;
using EmuDOS.Services;
using EmuDOS.ViewModels;

namespace EmuDOS;

/// <summary>
/// The per-game detail card: a floating card over the shelf (no dimming, stays on top of the app)
/// with a 4:3 art/video-snap banner, meta and activity pills, and Play / Favorite / "…" actions.
/// Imperative (Populate-style), modelled on Emutastic's card and themed with EmuDOS's tokens.
/// </summary>
public partial class GameDetailWindow : Window
{
    private readonly GameTile _tile;
    private readonly AppServices _services;
    private readonly Action _onPlay;
    private readonly IReadOnlyList<(string Label, Action Run)> _overflow;
    private bool _isFavorite;

    public GameDetailWindow(GameTile tile, AppServices services, Action onPlay,
                            IReadOnlyList<(string Label, Action Run)> overflow)
    {
        InitializeComponent();
        _tile = tile;
        _services = services;
        _onPlay = onPlay;
        _overflow = overflow;
        Populate();
    }

    private void Populate()
    {
        ArtPlaceholderText.Text = _tile.Title;
        GameTitle.Text = _tile.Title;
        MachineTag.Text = "DOS";

        if (_tile.Cover is { } cover)
        {
            HeaderImage.Source = cover;
            HeaderImage.Visibility = Visibility.Visible;
            ArtPlaceholderText.Visibility = Visibility.Collapsed;
        }

        var g = _services.Library.GetGame(_tile.Id) ?? _tile.Game; // fresh stats
        _isFavorite = g.IsFavorite;
        UpdateFavorite();
        PopulateStats(g);

        if (_services.Store.ReadMetadata(_tile.Game.GameboxPath) is { } md)
            PopulateMetadata(md);
        else
            _ = LoadMetadataAsync();

        _ = LoadSnapAsync();
    }

    // Fetch descriptive metadata on demand (for games imported before metadata existed) and show it
    // under the title; stored as the gamebox metadata.json + cached so it survives delete/re-import.
    private async Task LoadMetadataAsync()
    {
        try
        {
            var md = await _services.Art.FetchMetadataAsync(_tile.Title);
            if (md is null || _closed)
                return;
            var root = _tile.Game.GameboxPath;
            _services.Store.WriteMetadata(root, md);
            _services.ArtCache.StashMetadata(_tile.Title, Path.Combine(root, "metadata.json"));
            PopulateMetadata(md);
        }
        catch { /* leave the meta pills empty */ }
    }

    private void PopulateStats(LibraryGame g)
    {
        StatPlayed.Text = g.PlayCount == 0
            ? "Never played"
            : $"{g.PlayCount} play{(g.PlayCount == 1 ? "" : "s")}";
        if (g.TotalPlayTimeSeconds > 0)
        {
            StatPlayTime.Text = FormatDuration(g.TotalPlayTimeSeconds);
            PlayTimePill.Visibility = Visibility.Visible;
        }
    }

    private void PopulateMetadata(GameMetadata md)
    {
        SetPill(YearPill, GameYear, md.Year);
        SetPill(DeveloperPill, GameDeveloper, string.IsNullOrWhiteSpace(md.Developer) ? md.Publisher : md.Developer);
        SetPill(GenrePill, GameGenre, md.Genre);

        if (!string.IsNullOrWhiteSpace(md.Description))
        {
            GameDescription.Text = md.Description;
            GameDescriptionScroll.Visibility = Visibility.Visible;
        }
    }

    private static void SetPill(Border pill, TextBlock text, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;
        text.Text = value;
        pill.Visibility = Visibility.Visible;
    }

    public static string FormatDuration(long seconds) => seconds switch
    {
        < 60 => $"{seconds}s",
        < 3600 => $"{seconds / 60}m",
        < 360000 => $"{seconds / 3600.0:0.0}h",
        _ => $"{seconds / 3600}h",
    };

    private void UpdateFavorite()
    {
        FavoriteButton.Content = _isFavorite ? "♥  Favorited" : "♡  Favorite";
        FavoriteButton.Foreground = _isFavorite
            ? new SolidColorBrush((Color)FindResource("AccentColor"))
            : (Brush)FindResource("TextPrimary");
        FavoriteBadge.Visibility = _isFavorite ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnFavorite(object sender, RoutedEventArgs e)
    {
        _isFavorite = !_isFavorite;
        _services.Library.SetFavorite(_tile.Id, _isFavorite);
        _tile.IsFavorite = _isFavorite; // live-updates the shelf heart badge
        UpdateFavorite();
    }

    private void OnPlay(object sender, RoutedEventArgs e)
    {
        Close();
        _onPlay();
    }

    private void OnMore(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = (UIElement)sender, Placement = PlacementMode.Top };
        foreach (var (label, run) in _overflow)
        {
            var captured = run;
            var item = new MenuItem { Header = label };
            item.Click += (_, _) => { Close(); captured(); };
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    private void OnClose(object sender, MouseButtonEventArgs e) => Close();

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
            Close();
    }

    // ── Video snap (ScreenScraper, cached in the retained Snaps folder; banner placeholder → crossfade) ──
    private LibVLCSharp.Shared.MediaPlayer? _vlcPlayer;
    private WriteableBitmap? _videoBitmap;
    private IntPtr _videoBuffer;
    private bool _closed, _crossfadeDone;

    private async Task LoadSnapAsync()
    {
        try
        {
            var snapPath = Path.Combine(_services.Paths.SnapsDir, SnapKey() + ".mp4");
            if (!File.Exists(snapPath))
            {
                if (!await _services.Art.FetchSnapAsync(_tile.Title, snapPath) || _closed)
                    return;
            }
            if (_closed || !File.Exists(snapPath))
                return;
            await PlaySnapVideoAsync(snapPath);
        }
        catch { /* no snap — the banner keeps showing the cover art */ }
    }

    private string SnapKey()
    {
        var id = string.IsNullOrWhiteSpace(_tile.Game.CanonicalId) ? _tile.Title : _tile.Game.CanonicalId!;
        return string.Concat(id.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim();
    }

    private async Task PlaySnapVideoAsync(string mp4Path)
    {
        const int w = 320, h = 240, stride = w * 4; // ScreenScraper snaps are 4:3 320x240
        _crossfadeDone = false;

        if (_videoBuffer != IntPtr.Zero)
            Marshal.FreeHGlobal(_videoBuffer);
        _videoBuffer = Marshal.AllocHGlobal(stride * h);
        _videoBitmap = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgr32, null);
        VideoImage.Source = _videoBitmap;

        IntPtr bufferPtr = _videoBuffer;
        var libVLC = await VideoPlaybackService.Instance.GetLibVLCAsync();
        if (libVLC is null || _closed)
            return;

        await Task.Run(() =>
        {
            var player = new LibVLCSharp.Shared.MediaPlayer(libVLC);
            player.SetVideoFormat("RV32", (uint)w, (uint)h, (uint)stride);
            player.SetVideoCallbacks(
                (IntPtr opaque, IntPtr planes) => { Marshal.WriteIntPtr(planes, bufferPtr); return IntPtr.Zero; },
                null,
                (IntPtr opaque, IntPtr picture) => Dispatcher.BeginInvoke(() =>
                {
                    if (_videoBitmap is null || _videoBuffer == IntPtr.Zero)
                        return;
                    _videoBitmap.WritePixels(new Int32Rect(0, 0, w, h), _videoBuffer, stride * h, stride);

                    if (!_crossfadeDone)
                    {
                        _crossfadeDone = true;
                        VideoImage.Visibility = Visibility.Visible;
                        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(400));
                        fade.Completed += (_, _) => HeaderImage.Visibility = Visibility.Collapsed;
                        HeaderImage.BeginAnimation(OpacityProperty, fade);
                    }
                }));

            using var media = new LibVLCSharp.Shared.Media(libVLC, mp4Path, LibVLCSharp.Shared.FromType.FromPath);
            media.AddOption(":input-repeat=65535"); // loop forever

            if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
            {
                try { player.Dispose(); } catch { }
                return;
            }

            bool keep = false;
            try
            {
                Dispatcher.Invoke(() =>
                {
                    if (_closed)
                        return;
                    _vlcPlayer = player;
                    player.Play(media);
                    keep = true;
                });
            }
            catch (TaskCanceledException) { }

            if (!keep)
                try { player.Dispose(); } catch { }
        });
    }

    protected override void OnClosed(EventArgs e)
    {
        _closed = true;
        var player = _vlcPlayer;
        _vlcPlayer = null;
        var buffer = _videoBuffer;
        _videoBuffer = IntPtr.Zero; // display callback sees Zero and no-ops from here on
        Task.Run(() =>
        {
            try { player?.Stop(); } catch { }
            try { player?.Dispose(); } catch { }
            // Free the frame buffer ONLY after the player is fully disposed, so VLC's decode
            // thread can't write into freed memory (that use-after-free was the click-away crash).
            if (buffer != IntPtr.Zero)
                Marshal.FreeHGlobal(buffer);
        });
        base.OnClosed(e);
    }
}
