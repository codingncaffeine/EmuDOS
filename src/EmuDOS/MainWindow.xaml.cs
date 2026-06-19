using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using EmuDOS.Controls;
using EmuDOS.Core.Downloads;
using EmuDOS.Core.Engine.DosBoxPure;
using EmuDOS.ViewModels;

namespace EmuDOS;

/// <summary>Interaction logic for MainWindow.xaml.</summary>
public partial class MainWindow : Window
{
    private GameTile? _dragTile;
    private ShelfPanel? _dragPanel;
    private Point _grabOffset;
    private bool _didDrag;

    public MainWindow()
    {
        InitializeComponent();
        DarkChrome.Apply(this);
    }

    private MainViewModel? Vm => DataContext as MainViewModel;

    private void OnPreferences(object sender, RoutedEventArgs e)
    {
        var services = ((App)Application.Current).Services;
        new PreferencesWindow(services) { Owner = this }.ShowDialog();
    }

    private void OnBoxRightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: GameTile tile } element)
            return;

        var menu = new ContextMenu { PlacementTarget = element, Placement = PlacementMode.MousePoint };

        var preferences = new MenuItem { Header = "Preferences" };
        preferences.Click += (_, _) => OpenOptions(tile);

        var openInDos = new MenuItem { Header = "Open in DOS" };
        openInDos.Click += async (_, _) => await LaunchGameAsync(tile, bootToDos: true);

        var manual = new MenuItem { Header = "Download manual" };
        manual.Click += async (_, _) => await DownloadManualAsync(tile);

        menu.Items.Add(preferences);
        menu.Items.Add(openInDos);
        menu.Items.Add(manual);
        menu.IsOpen = true;
        e.Handled = true;
    }

    private async Task DownloadManualAsync(GameTile tile)
    {
        if (Vm is null)
            return;

        var services = ((App)Application.Current).Services;
        Vm.Report($"Downloading manual for {tile.Title}…", busy: true);
        try
        {
            var dir = Path.Combine(services.Paths.ManualsDir, SanitizeName(tile.Title));
            var path = await services.Manuals.FetchManualAsync(tile.Title, dir);
            if (path is null)
            {
                Vm.Report($"No manual found for {tile.Title}.", busy: false);
                return;
            }

            Vm.Report($"Manual saved to {dir}", busy: false);
            try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
            catch { /* no PDF handler — the file is saved regardless */ }
        }
        catch (Exception ex)
        {
            Vm.Report($"Manual download failed: {ex.Message}", busy: false);
        }
    }

    private static string SanitizeName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name.Trim();
    }

    private void OpenOptions(GameTile tile)
    {
        var services = ((App)Application.Current).Services;
        new PreferencesWindow(services, tile) { Owner = this }.ShowDialog();
    }

    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (Vm is null)
            return;

        if (e.Key == Key.F2)
        {
            Vm.IsEditMode = !Vm.IsEditMode;
            e.Handled = true;
        }
        else if (e.Key == Key.S && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            SaveLayout();
            e.Handled = true;
        }
        else if (e.Key == Key.A && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            Vm.SelectAll();
            e.Handled = true;
        }
        else if (e.Key == Key.Delete)
        {
            DeleteSelected();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            Vm.ClearSelection();
        }
    }

    private void DeleteSelected()
    {
        var selected = Vm!.Games.Where(g => g.IsSelected).ToList();
        if (selected.Count == 0)
            return;

        var names = selected.Count <= 3
            ? string.Join(", ", selected.Select(g => g.Title))
            : $"{selected.Count} games";

        if (ConfirmDialog.Show(this, "Delete from library",
                $"Remove {names} from your library?\n\nThe box art is kept, so re-importing won't re-download it.",
                "Delete"))
            Vm.DeleteGames(selected);
    }

    private void OnBoxMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (Vm?.IsEditMode != true || sender is not FrameworkElement fe
            || fe.DataContext is not GameTile tile)
            return;

        _dragPanel = FindPanel(fe);
        if (_dragPanel is null)
            return;

        _dragTile = tile;
        _grabOffset = e.GetPosition(fe);
        _didDrag = false;
        fe.CaptureMouse();
        e.Handled = true;
    }

    private void OnBoxMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragTile is null || _dragPanel is null || e.LeftButton != MouseButtonState.Pressed)
            return;

        var p = e.GetPosition(_dragPanel);
        _dragTile.ManualLeft = p.X - _grabOffset.X;
        _dragTile.ManualBottom = p.Y - _grabOffset.Y + _dragTile.BoxHeight;
        _didDrag = true;
        _dragPanel.InvalidateArrange();
    }

    private async void OnBoxMouseUp(object sender, MouseButtonEventArgs e)
    {
        (sender as FrameworkElement)?.ReleaseMouseCapture();

        if (Vm?.IsEditMode == true && _dragTile is not null && _dragPanel is not null && _didDrag)
        {
            // Full freedom — no snapping on either axis. You place each box exactly so I can
            // read the shelf landings, edge margins, and spacing from where you put them.
            _dragPanel.InvalidateArrange();
            _dragTile = null;
            _dragPanel = null;
            e.Handled = true;
            return;
        }

        _dragTile = null;
        _dragPanel = null;

        if (Vm?.IsEditMode == true || (sender as FrameworkElement)?.DataContext is not GameTile tile)
            return;

        // Ctrl+click toggles selection (for delete); a plain click launches.
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            tile.IsSelected = !tile.IsSelected;
            e.Handled = true;
            return;
        }

        Vm.ClearSelection();
        await LaunchGameAsync(tile);
    }

    /// <summary>Re-apply a saved calibration layout (manual box positions) by title.</summary>
    public void LoadSavedLayout()
    {
        if (Vm is null)
            return;

        var path = Path.Combine(@"C:\Users\gamer\source\repos\EmuDOS\local-notes", "layout.json");
        if (!File.Exists(path))
            return;

        try
        {
            var entries = JsonSerializer.Deserialize<LayoutEntry[]>(File.ReadAllText(path)) ?? [];
            foreach (var entry in entries)
            {
                var tile = Vm.Games.FirstOrDefault(t => t.Title == entry.Title);
                if (tile is not null)
                {
                    tile.ManualLeft = entry.Left;
                    tile.ManualBottom = entry.Bottom;
                }
            }

            FindVisualChild<ShelfPanel>(this)?.InvalidateArrange();
        }
        catch
        {
            // Calibration file is dev-only; ignore any read/parse trouble.
        }
    }

    private sealed record LayoutEntry(string Title, double Left, double Bottom);

    private static T? FindVisualChild<T>(DependencyObject root) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
                return match;
            if (FindVisualChild<T>(child) is { } found)
                return found;
        }

        return null;
    }

    private void SaveLayout()
    {
        if (Vm is null)
            return;

        var placed = Vm.Games
            .Where(t => t.IsManuallyPlaced)
            .Select(t => new
            {
                t.Title,
                Left = Math.Round(t.ManualLeft!.Value, 1),
                Bottom = Math.Round(t.ManualBottom!.Value, 1),
                Width = Math.Round(t.BoxWidth, 1),
                Height = Math.Round(t.BoxHeight, 1),
            })
            .OrderBy(o => o.Bottom)
            .ThenBy(o => o.Left)
            .ToList();

        var path = Path.Combine(
            @"C:\Users\gamer\source\repos\EmuDOS\local-notes", "layout.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(placed, new JsonSerializerOptions { WriteIndented = true }));
        Vm.Report($"Saved {placed.Count} box positions to layout.json.", busy: false);
    }

    private static ShelfPanel? FindPanel(DependencyObject? d)
    {
        while (d is not null)
        {
            if (d is ShelfPanel panel)
                return panel;
            d = VisualTreeHelper.GetParent(d);
        }

        return null;
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (Vm is not null && e.Data.GetData(DataFormats.FileDrop) is string[] paths && paths.Length > 0)
            await Vm.HandleDropAsync(paths);
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    /// <summary>Re-attempt covers for games still missing one (after an art login/key change).</summary>
    public Task RefetchMissingArtAsync() => Vm?.FetchMissingArtAsync() ?? Task.CompletedTask;

    /// <summary>Launch the first game on the shelf (used by the auto-play dev hook).</summary>
    public async Task PlayFirstAsync()
    {
        if (Vm?.Games.Count > 0)
            await LaunchGameAsync(Vm.Games[0]);
    }

    private async Task LaunchGameAsync(GameTile tile, bool bootToDos = false)
    {
        if (Vm is null)
            return;

        var services = ((App)Application.Current).Services;

        // The core is downloaded on demand (never bundled), so fetch it on first launch.
        if (!services.Downloads.IsInstalled(AssetManifest.DosBoxPure))
        {
            Vm.Report("Downloading DOSBox Pure core…", busy: true);
            var download = await services.Downloads.DownloadAsync(AssetManifest.DosBoxPure);
            if (!download.Success)
            {
                Vm.Report($"Core download failed: {download.Error}", busy: false);
                return;
            }
        }

        var instance = services.Store.Resolve(tile.Game.GameboxPath);
        if (bootToDos)
        {
            // No game launch — just configure + drop to the C: prompt (content is mounted as C:)
            // so the user can browse files and run the game's SETUP (e.g. to choose Roland MT-32).
            instance = instance with { Profile = instance.Profile with { Launch = new Core.Model.LaunchSpec() } };
        }

        var engine = new DosBoxPureEngine(
            services.Downloads.InstalledPath(AssetManifest.DosBoxPure), services.Paths.SystemDir);
        services.Library.RecordPlay(tile.Id);

        new EmulatorWindow(engine, instance) { Owner = this }.Show();
        Vm.ClearStatus();
    }
}
