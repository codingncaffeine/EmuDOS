using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using EmuDOS.Controls;
using EmuDOS.Core.Downloads;
using EmuDOS.Core.Engine.DosBoxPure;
using EmuDOS.Core.Model;
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

    private async void OnDownloadMissingArt(object sender, RoutedEventArgs e)
    {
        if (Vm is not null)
            await Vm.FetchMissingArtAsync();
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

        var boxArt = new MenuItem { Header = "Download box art" };
        boxArt.Click += async (_, _) => await (Vm?.DownloadArtAsync(tile) ?? Task.CompletedTask);

        var customArt = new MenuItem { Header = "Set box art from file…" };
        customArt.Click += (_, _) => SetCustomArt(tile);

        var manual = new MenuItem { Header = "Download manual" };
        manual.Click += async (_, _) => await DownloadManualAsync(tile);

        menu.Items.Add(preferences);
        menu.Items.Add(openInDos);
        menu.Items.Add(boxArt);
        menu.Items.Add(customArt);
        menu.Items.Add(manual);

        // For disc-based games (e.g. an installed Windows machine), let the user add more discs —
        // each shows up in dosbox_pure's start menu to mount as D: (swap CDs without leaving the OS).
        if (IsDiscGame(tile))
        {
            var addDisc = new MenuItem { Header = "Add disc…" };
            addDisc.Click += (_, _) => AddDisc(tile);
            menu.Items.Add(addDisc);
        }

        // A "Run" submenu of executables we've used before plus any found in the content, so the
        // user can pick the right one when the default launch doesn't land on a runnable program.
        var services = ((App)Application.Current).Services;
        var executables = OrderedExecutables(
            services.Store.ReadState(tile.Game.GameboxPath),
            ScanExecutables(Path.Combine(tile.Game.GameboxPath, "content")));
        if (executables.Count > 0)
        {
            var run = new MenuItem { Header = "Run" };
            foreach (var exe in executables.Take(25))
            {
                var item = new MenuItem { Header = exe };
                item.Click += async (_, _) => await LaunchGameAsync(tile, executableOverride: exe);
                run.Items.Add(item);
            }
            menu.Items.Add(run);
        }

        menu.IsOpen = true;
        e.Handled = true;
    }

    private static readonly string[] ExecutableExtensions = [".exe", ".com", ".bat"];

    /// <summary>A configuration/installer program, not the game — shouldn't become the default launch.</summary>
    private static bool IsSetupLike(string executable)
    {
        var name = Path.GetFileNameWithoutExtension(executable).ToLowerInvariant();
        return name.Contains("setup") || name.Contains("install") || name.Contains("config");
    }

    /// <summary>DOS-relative paths of runnable files under the content (minus the DOSBox wrapper).</summary>
    private static List<string> ScanExecutables(string contentDir)
    {
        var found = new List<string>();
        if (!Directory.Exists(contentDir))
            return found;
        try
        {
            foreach (var file in Directory.EnumerateFiles(contentDir, "*.*", SearchOption.AllDirectories))
            {
                if (!ExecutableExtensions.Contains(Path.GetExtension(file).ToLowerInvariant()))
                    continue;
                if (Core.Import.DosExecutables.IsRuntimeHelper(file))
                    continue; // DOS extenders / the DOSBox wrapper aren't launch targets
                found.Add(Path.GetRelativePath(contentDir, file).Replace('/', '\\'));
            }
        }
        catch
        {
            // Best-effort scan; an unreadable folder just yields fewer options.
        }
        return found;
    }

    private static List<string> OrderedExecutables(GameUserState state, List<string> scanned)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ordered = new List<string>();
        foreach (var exe in state.KnownExecutables.Concat(scanned))
            if (!string.IsNullOrWhiteSpace(exe) && seen.Add(exe))
                ordered.Add(exe);
        return ordered;
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

    private static readonly string[] DiscExtensions = [".iso", ".cue", ".bin", ".chd"];

    /// <summary>A disc-based gamebox — one whose content holds a CD image (the only kind the
    /// .m3u8 disc-swap applies to).</summary>
    private static bool IsDiscGame(GameTile tile)
    {
        var content = Path.Combine(tile.Game.GameboxPath, "content");
        if (!Directory.Exists(content))
            return false;
        try
        {
            return Directory.EnumerateFiles(content)
                .Any(f => DiscExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()));
        }
        catch { return false; }
    }

    /// <summary>Copy a chosen disc image into the gamebox content so it joins the disc-swap menu.</summary>
    private void AddDisc(GameTile tile)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = $"Add a disc to {tile.Title}",
            Filter = "Disc images|*.iso;*.cue;*.bin;*.chd|All files|*.*",
        };
        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            var content = Path.Combine(tile.Game.GameboxPath, "content");
            Directory.CreateDirectory(content);
            File.Copy(dialog.FileName, Path.Combine(content, Path.GetFileName(dialog.FileName)), overwrite: true);

            // A .cue references .bin track files alongside it — bring them along too.
            if (Path.GetExtension(dialog.FileName).Equals(".cue", StringComparison.OrdinalIgnoreCase))
            {
                var sourceDir = Path.GetDirectoryName(dialog.FileName)!;
                foreach (var bin in Directory.EnumerateFiles(sourceDir, "*.bin"))
                    File.Copy(bin, Path.Combine(content, Path.GetFileName(bin)), overwrite: true);
            }

            Vm?.Report($"Added {Path.GetFileName(dialog.FileName)} to {tile.Title} — launch it, then pick the disc in the start menu to mount it as D:.", busy: false);
        }
        catch (Exception ex)
        {
            Vm?.Report($"Couldn't add disc: {ex.Message}", busy: false);
        }
    }

    private void SetCustomArt(GameTile tile)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = $"Choose box art for {tile.Title}",
            Filter = "Images|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp|All files|*.*",
        };
        if (dialog.ShowDialog(this) != true)
            return;

        try { Vm?.SetBoxArt(tile, File.ReadAllBytes(dialog.FileName)); }
        catch (Exception ex) { Vm?.Report($"Couldn't set box art: {ex.Message}", busy: false); }
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

    // ── Drag an image onto a box to set its cover (local file or straight from a browser) ──

    private static readonly HttpClient ImageHttp = new();
    private static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp"];

    private void OnBoxArtDragOver(object sender, DragEventArgs e)
    {
        e.Effects = HasDroppableImage(e.Data) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnBoxArtDrop(object sender, DragEventArgs e)
    {
        if (Vm is null || sender is not FrameworkElement { DataContext: GameTile tile })
            return;
        if (!HasDroppableImage(e.Data))
            return; // let a non-image (e.g. a game folder) fall through to import

        e.Handled = true;
        Vm.Report($"Adding box art for {tile.Title}…", busy: true);
        var bytes = await ExtractDroppedImageAsync(e.Data);
        if (bytes is not null)
            Vm.SetBoxArt(tile, bytes);
        else
            Vm.Report("Couldn't read the dropped image.", busy: false);
    }

    private static bool HasDroppableImage(IDataObject data) =>
        (data.GetData(DataFormats.FileDrop) is string[] files && files.Any(IsImageFile))
        || UrlFrom(data) is not null
        || data.GetDataPresent(DataFormats.Bitmap);

    private static bool IsImageFile(string path) =>
        ImageExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    private static async Task<byte[]?> ExtractDroppedImageAsync(IDataObject data)
    {
        if (data.GetData(DataFormats.FileDrop) is string[] files
            && files.FirstOrDefault(IsImageFile) is { } file)
        {
            try { return await File.ReadAllBytesAsync(file); } catch { /* fall through */ }
        }

        if (UrlFrom(data) is { } url)
        {
            try { return await ImageHttp.GetByteArrayAsync(url); } catch { /* fall through */ }
        }

        if (data.GetData(DataFormats.Bitmap) is BitmapSource bitmap)
        {
            try
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var ms = new MemoryStream();
                encoder.Save(ms);
                return ms.ToArray();
            }
            catch { /* fall through */ }
        }

        return null;
    }

    // Browsers expose a dragged image's address in one of these formats.
    private static string? UrlFrom(IDataObject data)
    {
        foreach (var format in new[] { "UniformResourceLocatorW", "UniformResourceLocator", DataFormats.Text })
        {
            if (!data.GetDataPresent(format))
                continue;

            var raw = data.GetData(format);
            string? text = raw as string;
            if (text is null && raw is MemoryStream ms)
            {
                var bytes = ms.ToArray();
                text = format == "UniformResourceLocatorW"
                    ? Encoding.Unicode.GetString(bytes)
                    : Encoding.Default.GetString(bytes);
            }

            var first = text?.Split('\n', '\r').FirstOrDefault()?.Trim().Trim('\0');
            if (first is not null
                && (first.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                    || first.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
                return first;
        }

        return null;
    }

    /// <summary>Re-attempt covers for games still missing one (after an art login/key change).</summary>
    public Task RefetchMissingArtAsync() => Vm?.FetchMissingArtAsync() ?? Task.CompletedTask;

    /// <summary>Launch the first game on the shelf (used by the auto-play dev hook).</summary>
    public async Task PlayFirstAsync()
    {
        if (Vm?.Games.Count > 0)
            await LaunchGameAsync(Vm.Games[0]);
    }

    private async Task LaunchGameAsync(GameTile tile, bool bootToDos = false, string? executableOverride = null)
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
            instance = instance with { Profile = instance.Profile with { Launch = new LaunchSpec() } };
        }
        else
        {
            // Pick the executable: an explicit Run-menu choice wins; otherwise the one the user
            // last chose for this game (the running tally), falling back to the configured default.
            // The remembered choice beats the configured exe so a bad guess (e.g. a DOS extender)
            // doesn't keep stranding the user once they've found the real launcher.
            var state = services.Store.ReadState(tile.Game.GameboxPath);
            var configured = instance.Profile.Launch.Executable;
            var chosen = executableOverride
                ?? (string.IsNullOrWhiteSpace(state.LastExecutable) ? configured : state.LastExecutable);
            if (!string.Equals(chosen, configured, StringComparison.OrdinalIgnoreCase))
                instance = instance with
                {
                    Profile = instance.Profile with { Launch = instance.Profile.Launch with { Executable = chosen } },
                };

            // Only an explicit Run-menu pick becomes the new default — and not a one-off setup
            // program, so "go tweak SETUP.EXE" doesn't replace the game as the normal launch.
            if (executableOverride is not null && !IsSetupLike(executableOverride))
                services.Store.WriteState(tile.Game.GameboxPath, state.WithExecutable(executableOverride));
        }

        var engine = new DosBoxPureEngine(
            services.Downloads.InstalledPath(AssetManifest.DosBoxPure), services.Paths.SystemDir);
        services.Library.RecordPlay(tile.Id);

        new EmulatorWindow(engine, instance) { Owner = this }.Show();
        Vm.ClearStatus();
    }
}
