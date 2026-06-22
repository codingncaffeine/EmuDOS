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
/// The per-game detail card: box art, title, play stats, and the action row (Play / Favorite / "…").
/// A transparent full-window scrim over the shelf; click outside or Esc to dismiss. Imperative
/// (PopulateData-style) — no MVVM binding. Later phases add metadata and a looping video snap.
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
        TitleText.Text = _tile.Title;
        ArtImage.Source = _tile.Cover; // already a frozen BitmapImage loaded by the tile

        var g = _services.Library.GetGame(_tile.Id) ?? _tile.Game; // fresh stats
        _isFavorite = g.IsFavorite;
        UpdateFavButton();
        StatsText.Text = BuildStats(g);

        if (_services.Store.ReadMetadata(_tile.Game.GameboxPath) is { } md)
            PopulateMetadata(md);

        _ = LoadSnapAsync();
    }

    private void PopulateMetadata(GameMetadata md)
    {
        AddMetaLine("Genre", md.Genre);
        AddMetaLine("Year", md.Year);
        AddMetaLine("Developer", md.Developer);
        AddMetaLine("Publisher", md.Publisher);

        if (!string.IsNullOrWhiteSpace(md.Description))
        {
            BodyPanel.Children.Add(new TextBlock
            {
                Text = "Description",
                Foreground = (Brush)FindResource("TextSecondary"),
                FontSize = 11,
                Margin = new Thickness(0, 14, 0, 3),
            });
            BodyPanel.Children.Add(new TextBlock
            {
                Text = md.Description,
                Foreground = (Brush)FindResource("TextPrimary"),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
            });
        }
    }

    private void AddMetaLine(string label, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 5) };
        row.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = (Brush)FindResource("TextSecondary"),
            FontSize = 12,
            Width = 86,
        });
        row.Children.Add(new TextBlock
        {
            Text = value,
            Foreground = (Brush)FindResource("TextPrimary"),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
        });
        BodyPanel.Children.Add(row);
    }

    private static string BuildStats(LibraryGame g)
    {
        var parts = new List<string>
        {
            g.PlayCount == 0 ? "Never played" : $"Played {g.PlayCount} time{(g.PlayCount == 1 ? "" : "s")}",
        };
        if (g.TotalPlayTimeSeconds > 0)
            parts.Add($"{FormatDuration(g.TotalPlayTimeSeconds)} played");
        if (g.LastPlayed is { } lp)
            parts.Add($"Last played {lp.LocalDateTime:d}");
        return string.Join("   ·   ", parts);
    }

    public static string FormatDuration(long seconds) => seconds switch
    {
        < 60 => $"{seconds}s",
        < 3600 => $"{seconds / 60}m",
        < 360000 => $"{seconds / 3600.0:0.0}h",
        _ => $"{seconds / 3600}h",
    };

    private void UpdateFavButton()
    {
        FavButton.Content = _isFavorite ? "★ Favorited" : "☆ Favorite";
        FavButton.Foreground = _isFavorite
            ? new SolidColorBrush((Color)FindResource("AccentColor"))
            : (Brush)FindResource("TextPrimary");
    }

    private void OnFavorite(object sender, RoutedEventArgs e)
    {
        _isFavorite = !_isFavorite;
        _services.Library.SetFavorite(_tile.Id, _isFavorite);
        _tile.IsFavorite = _isFavorite; // live-updates the shelf heart badge
        UpdateFavButton();
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

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private void OnScrimDown(object sender, MouseButtonEventArgs e) => Close();

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
            Close();
    }

    // ── Video snap (ScreenScraper, cached in the retained Snaps folder; placeholder → crossfade) ──
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
                // Fetch from ScreenScraper into the retained cache (survives game deletion).
                if (!await _services.Art.FetchSnapAsync(_tile.Title, snapPath) || _closed)
                    return;
            }
            if (_closed || !File.Exists(snapPath))
                return;

            SnapPlaceholder.Source = _tile.Cover; // box-art placeholder until the first video frame
            SnapArea.Visibility = Visibility.Visible;
            await PlaySnapVideoAsync(snapPath);
        }
        catch { /* no snap available — leave the snap area hidden */ }
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
        SnapVideo.Source = _videoBitmap;

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
                        SnapVideo.Visibility = Visibility.Visible;
                        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(400));
                        fade.Completed += (_, _) => SnapPlaceholder.Visibility = Visibility.Collapsed;
                        SnapPlaceholder.BeginAnimation(OpacityProperty, fade);
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
                // Stash + start atomically on the UI thread so OnClosed can't dispose mid-Play.
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
        if (player is not null)
            Task.Run(() => { try { player.Stop(); } catch { } try { player.Dispose(); } catch { } });
        if (_videoBuffer != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_videoBuffer);
            _videoBuffer = IntPtr.Zero;
        }
        base.OnClosed(e);
    }
}
