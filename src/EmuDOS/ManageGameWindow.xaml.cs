using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using EmuDOS.Core.Library;
using EmuDOS.Services;

namespace EmuDOS;

/// <summary>
/// Per-game management: view and delete in-game saves, save states (with thumbnails), screenshots,
/// and videos, plus an autosaving notes pane. Everything is per-game, read straight from the gamebox.
/// </summary>
public partial class ManageGameWindow : Window
{
    private readonly Gamebox _box;
    private readonly AppServices _services;
    private readonly string _title;
    private readonly string _gameboxPath;
    private EmuDOS.Core.Model.GameProfile? _profile;
    private bool _displayLoaded;
    private string _notesOnDisk = string.Empty;

    public ManageGameWindow(AppServices services, LibraryGame game)
    {
        InitializeComponent();
        _services = services;
        _title = game.Title;
        _gameboxPath = game.GameboxPath;
        _box = new Gamebox(game.GameboxPath);
        Title = $"Manage — {game.Title}";
        HeaderText.Text = game.Title;

        LoadMediaLists();
        LoadDisplaySettings();
        LoadNotes();

        NotesBox.LostKeyboardFocus += (_, _) => SaveNotes();
        Closing += (_, _) => SaveNotes();
    }

    // ── Display tab (per-game frame-rate lock + 3dfx) ─────────────────────────────────────────
    private static readonly string[] FpsOptions = ["Off", "30", "35", "50", "60", "70", "90", "120", "144"];

    private void LoadDisplaySettings()
    {
        try { _profile = _services.Store.ReadProfile(_gameboxPath); }
        catch { _profile = null; }

        FpsLockBox.ItemsSource = FpsOptions;
        Hw3dfxBox.ItemsSource = Enum.GetNames<EmuDOS.Core.Model.Hardware3dfxMode>();

        var m = _profile?.Machine;
        FpsLockBox.SelectedItem = m is { FpsLock: > 0 } ? m.FpsLock.ToString() : "Off";
        Hw3dfxBox.SelectedItem = (m?.Hardware3dfx ?? EmuDOS.Core.Model.Hardware3dfxMode.Default).ToString();
        _displayLoaded = true;
    }

    private void OnDisplaySettingChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (!_displayLoaded || _profile is null)
            return;
        int fps = int.TryParse(FpsLockBox.SelectedItem as string, out var v) ? v : 0;
        var mode = Enum.TryParse<EmuDOS.Core.Model.Hardware3dfxMode>(Hw3dfxBox.SelectedItem as string, out var md)
            ? md : EmuDOS.Core.Model.Hardware3dfxMode.Default;
        _profile = _profile with { Machine = _profile.Machine with { FpsLock = fps, Hardware3dfx = mode } };
        _services.Store.WriteProfile(_gameboxPath, _profile);
    }

    // ── Row model bound by the DataTemplates ──────────────────────────────────────────────
    public sealed class Row
    {
        public ImageSource? Thumb { get; init; }
        public Visibility ThumbVisibility => Thumb is null ? Visibility.Collapsed : Visibility.Visible;
        public string Primary { get; init; } = "";
        public string Secondary { get; init; } = "";
        public string Path { get; init; } = "";        // file to open/delete
        public SaveStateInfo? State { get; init; }       // set for save-state rows
    }

    private void LoadMediaLists()
    {
        // Save states (newest first, with thumbnails).
        var states = SaveStateStore.List(_box.SavesDir).Select(s => new Row
        {
            Thumb = LoadThumb(s.ThumbPath),
            Primary = s.Label ?? "Save state",
            Secondary = s.WhenUtc.ToLocalTime().ToString("g"),
            Path = s.StatePath,
            State = s,
        }).ToList();
        Bind(StatesList, StatesEmpty, states);

        Bind(ShotsList, ShotsEmpty, FileRows(_box.ScreenshotsDir, "*.png", thumb: true));
        Bind(VideosList, VideosEmpty, FileRows(_box.VideosDir, "*.mp4", thumb: false));

        Bind(SavesList, SavesEmpty, LoadInGameSaves());
        LoadExtras();
    }

    private void LoadExtras()
    {
        var rows = Directory.Exists(_box.ExtrasDir)
            ? Directory.EnumerateFiles(_box.ExtrasDir)
                .Where(f => f.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                         || f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                         || f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .Select(f => new Row { Thumb = LoadThumb(f), Primary = ExtraLabel(f), Path = f })
                .ToList()
            : new List<Row>();
        Bind(ExtrasList, ExtrasEmpty, rows);
    }

    // "wheel" -> "Logo", "ss" -> "Screenshot", etc. (the SS media type is the file name).
    private static string ExtraLabel(string path) =>
        System.IO.Path.GetFileNameWithoutExtension(path).ToLowerInvariant() switch
        {
            "wheel" => "Logo",
            "marquee" => "Marquee",
            "fanart" => "Fan art",
            "ss" => "Screenshot",
            "maps" => "Map",
            var other => char.ToUpperInvariant(other[0]) + other[1..],
        };

    private async void OnDownloadExtras(object sender, RoutedEventArgs e)
    {
        DownloadExtrasButton.IsEnabled = false;
        ExtrasStatus.Text = "Downloading from ScreenScraper…";
        try
        {
            var n = await _services.Art.FetchExtrasAsync(_title, _box.ExtrasDir);
            ExtrasStatus.Text = n > 0 ? $"Downloaded {n} item(s)." : "No extras found for this game.";
            LoadExtras();
        }
        catch (Exception ex)
        {
            ExtrasStatus.Text = $"Couldn't download: {ex.Message}";
        }
        finally
        {
            DownloadExtrasButton.IsEnabled = true;
        }
    }

    private void OnOpenExtra(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not Row row)
            return;
        var paths = (ExtrasList.ItemsSource as IEnumerable<Row>)?.Select(r => r.Path).ToList()
                    ?? new List<string> { row.Path };
        new ImageViewerWindow(paths, Math.Max(0, paths.IndexOf(row.Path))) { Owner = this }.Show();
    }

    // In-game saves differ by game type: Iso games persist to a *.pure.zip in saves/; folder games
    // write in place into content/, so we show what changed since the import baseline.
    private List<Row> LoadInGameSaves()
    {
        var hasPureZip = Directory.Exists(_box.SavesDir) &&
                         Directory.EnumerateFiles(_box.SavesDir, "*.pure.zip").Any();
        if (hasPureZip)
        {
            return Directory.EnumerateFiles(_box.SavesDir)
                .Where(f => System.IO.Path.GetFileName(f) is var n
                            && !n.StartsWith("state_", StringComparison.OrdinalIgnoreCase)
                            && !n.StartsWith('.'))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .Select(f => RowForFile(f, thumb: false))
                .ToList();
        }

        // Folder game: ensure a baseline exists (so future saves are tracked), then show the diff.
        ContentBaseline.CaptureIfMissing(_box.ContentDir, _box.SavesDir);
        return ContentBaseline.DiffSaves(_box.ContentDir, _box.SavesDir)
            .Select(rel =>
            {
                var full = System.IO.Path.Combine(_box.ContentDir, rel);
                var fi = new FileInfo(full);
                return new Row
                {
                    Primary = rel,
                    Secondary = $"{FormatSize(fi.Length)} · {fi.LastWriteTime:g}",
                    Path = full,
                };
            })
            .ToList();
    }

    private static List<Row> FileRows(string dir, string pattern, bool thumb)
    {
        if (!Directory.Exists(dir))
            return new List<Row>();
        return Directory.EnumerateFiles(dir, pattern)
            .OrderByDescending(f => File.GetLastWriteTimeUtc(f))
            .Select(f => RowForFile(f, thumb))
            .ToList();
    }

    private static Row RowForFile(string path, bool thumb)
    {
        var fi = new FileInfo(path);
        return new Row
        {
            Thumb = thumb ? LoadThumb(path) : null,
            Primary = fi.Name,
            Secondary = $"{FormatSize(fi.Length)} · {fi.LastWriteTime:g}",
            Path = path,
        };
    }

    private static void Bind(System.Windows.Controls.ListBox list, System.Windows.UIElement empty, List<Row> rows)
    {
        list.ItemsSource = rows;
        empty.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static ImageSource? LoadThumb(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return null;
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(path);
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bmp.DecodePixelWidth = 192;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    // ── Actions ───────────────────────────────────────────────────────────────────────────
    private void OnOpenRow(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not Row row)
            return;
        try
        {
            Process.Start(new ProcessStartInfo(row.Path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Couldn't open:\n{ex.Message}", "EmuDOS", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnShowInExplorer(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not Row row)
            return;
        try
        {
            if (File.Exists(row.Path))
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{row.Path}\"") { UseShellExecute = true });
            else if (System.IO.Path.GetDirectoryName(row.Path) is { } dir && Directory.Exists(dir))
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Couldn't open Explorer:\n{ex.Message}", "EmuDOS", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>Set when the user clicks "Load" on a save state — the caller launches the game restored
    /// to it once this (modal) window closes.</summary>
    public SaveStateInfo? StateToLaunch { get; private set; }

    private void OnLoadState(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not Row { State: { } state })
            return;
        StateToLaunch = state;
        DialogResult = true; // close; MainWindow launches the game with this state
        Close();
    }

    private void OnDeleteRow(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not Row row)
            return;
        var name = row.State is not null ? "this save state" : $"\"{row.Primary}\"";
        if (MessageBox.Show(this, $"Delete {name}? This can't be undone.", "Delete",
                MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK)
            return;
        try
        {
            if (row.State is not null)
                SaveStateStore.Delete(row.State);
            else if (File.Exists(row.Path))
                File.Delete(row.Path);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Couldn't delete:\n{ex.Message}", "EmuDOS", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        LoadMediaLists();
    }

    // ── Notes ───────────────────────────────────────────────────────────────────────────
    private void LoadNotes()
    {
        try
        {
            _notesOnDisk = File.Exists(_box.NotesPath) ? File.ReadAllText(_box.NotesPath) : string.Empty;
        }
        catch
        {
            _notesOnDisk = string.Empty;
        }
        NotesBox.Text = _notesOnDisk;
    }

    private void SaveNotes()
    {
        var text = NotesBox.Text;
        if (text == _notesOnDisk)
            return;
        try
        {
            if (string.IsNullOrEmpty(text))
            {
                if (File.Exists(_box.NotesPath))
                    File.Delete(_box.NotesPath);
            }
            else
            {
                Directory.CreateDirectory(_box.Root);
                File.WriteAllText(_box.NotesPath, text);
            }
            _notesOnDisk = text;
        }
        catch { /* best effort; keep the text in the box */ }
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (double)(1L << 30):0.#} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):0.#} MB",
        >= 1L << 10 => $"{bytes / (double)(1L << 10):0.#} KB",
        _ => $"{bytes} B",
    };
}
