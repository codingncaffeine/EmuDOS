using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
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

    private void UpdateFavButton() => FavButton.Content = _isFavorite ? "★ Favorited" : "☆ Favorite";

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
}
