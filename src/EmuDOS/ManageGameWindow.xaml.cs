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
    private string _notesOnDisk = string.Empty;

    public ManageGameWindow(AppServices services, LibraryGame game)
    {
        InitializeComponent();
        _box = new Gamebox(game.GameboxPath);
        Title = $"Manage — {game.Title}";
        HeaderText.Text = game.Title;

        LoadMediaLists();
        LoadNotes();

        NotesBox.LostKeyboardFocus += (_, _) => SaveNotes();
        Closing += (_, _) => SaveNotes();
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

        // In-game saves: the save folder's contents minus the save-state artifacts (shown above).
        var saves = !Directory.Exists(_box.SavesDir)
            ? new List<Row>()
            : Directory.EnumerateFiles(_box.SavesDir)
                .Where(f => !System.IO.Path.GetFileName(f).StartsWith("state_", StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .Select(f => RowForFile(f, thumb: false))
                .ToList();
        Bind(SavesList, SavesEmpty, saves);
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
