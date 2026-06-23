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
using EmuDOS.Core.Import;
using EmuDOS.Core.Library;
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
        WindowIcon.Apply(this); // force the taskbar (big) icon — WPF only sets the small one reliably
        // Click anywhere in the app (the shelf) to dismiss an open card — in addition to its ✕/Esc.
        // (Deactivated never fires for owned windows, so we close it from the owner's click instead.)
        PreviewMouseDown += (_, _) => _openCard?.Close();
    }

    private MainViewModel? Vm => DataContext as MainViewModel;

    private void OnPreferences(object sender, RoutedEventArgs e)
    {
        var services = ((App)Application.Current).Services;
        new PreferencesWindow(services) { Owner = this }.ShowDialog();
        Vm?.ReapplyBoxStyle(); // pick up a changed "Use 3D boxes by default"
    }

    private async void OnDownloadMissingArt(object sender, RoutedEventArgs e)
    {
        if (Vm is not null)
            await Vm.FetchMissingArtAsync();
    }

    private async void OnDownload3DArtAll(object sender, RoutedEventArgs e)
    {
        if (Vm is not null)
            await Vm.FetchAll3DArtAsync();
    }

    private void OnBoxRightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: GameTile tile } element)
            return;

        var menu = new ContextMenu { PlacementTarget = element, Placement = PlacementMode.MousePoint };
        var services = ((App)Application.Current).Services;

        // ── Play / favorite / quick-load ──
        var play = new MenuItem { Header = "▶  Play" };
        play.Click += async (_, _) => await LaunchGameAsync(tile);

        var favorite = new MenuItem { Header = tile.IsFavorite ? "♥  Favorited" : "♡  Favorite" };
        favorite.Click += (_, _) =>
        {
            tile.IsFavorite = !tile.IsFavorite;
            services.Library.SetFavorite(tile.Id, tile.IsFavorite);
        };

        // Launch straight into a save state (same as the Manage window's Load button).
        var loadState = new MenuItem { Header = "⏱  Load save state" };
        var states = SaveStateStore.List(Path.Combine(tile.Game.GameboxPath, "saves"));
        if (states.Count == 0)
            loadState.Items.Add(new MenuItem { Header = "No save states yet", IsEnabled = false });
        else
            foreach (var st in states)
            {
                var captured = st;
                var si = new MenuItem { Header = $"{st.Label ?? "Save state"} — {st.WhenUtc.ToLocalTime():g}" };
                si.Click += async (_, _) =>
                {
                    var bytes = SaveStateStore.ReadState(captured);
                    if (bytes is not null)
                        await LaunchGameAsync(tile, loadState: bytes);
                };
                loadState.Items.Add(si);
            }

        // ── Configure / launch ──
        var preferences = new MenuItem { Header = "⚙  Preferences" };
        preferences.Click += (_, _) => OpenOptions(tile);

        var manage = new MenuItem { Header = "🛠  Manage…" };
        manage.Click += (_, _) => OpenManage(tile);

        var openFolder = new MenuItem { Header = "📂  Open game folder" };
        openFolder.Click += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo(tile.Game.GameboxPath) { UseShellExecute = true }); }
            catch { /* folder may have been removed */ }
        };

        var openInDos = new MenuItem { Header = "🖥  Open in DOS" };
        openInDos.Click += async (_, _) => await LaunchGameAsync(tile, bootToDos: true);

        var launchParams = new MenuItem { Header = "⌨  Launch parameters…" };
        launchParams.Click += (_, _) => EditLaunchParameters(tile);

        // ── Artwork / metadata ──
        var boxArt = new MenuItem { Header = "🖼  Download box art" };
        boxArt.Click += async (_, _) => await (Vm?.DownloadArtAsync(tile) ?? Task.CompletedTask);

        var box3D = new MenuItem { Header = "🧊  Download 3D box art" };
        box3D.Click += async (_, _) => await (Vm?.Download3DArtAsync(tile) ?? Task.CompletedTask);

        var customArt = new MenuItem { Header = "📁  Set box art from file…" };
        customArt.Click += (_, _) => SetCustomArt(tile);

        // Per-game 2D/3D choice (overrides the global default; for when one style's art is poor).
        var boxStyle = new MenuItem { Header = "🎴  Box style" };
        foreach (var (label, style) in new[]
                 {
                     ("Default (follow global)", BoxStyle.Default),
                     ("2D box", BoxStyle.TwoD),
                     ("3D box", BoxStyle.ThreeD),
                 })
        {
            var captured = style;
            var item = new MenuItem { Header = label, IsCheckable = true, IsChecked = tile.StyleOverride == style };
            item.Click += (_, _) => Vm?.SetGameBoxStyle(tile, captured);
            boxStyle.Items.Add(item);
        }

        var rename = new MenuItem { Header = "✏  Rename from ScreenScraper…" };
        rename.Click += (_, _) => RenameFromScreenScraper(tile);

        var manual = new MenuItem { Header = "📖  Download manual" };
        manual.Click += async (_, _) => await DownloadManualAsync(tile);

        menu.Items.Add(play);
        menu.Items.Add(favorite);
        menu.Items.Add(loadState);
        menu.Items.Add(new Separator());
        menu.Items.Add(preferences);
        menu.Items.Add(manage);
        menu.Items.Add(openFolder);
        menu.Items.Add(openInDos);
        menu.Items.Add(launchParams);

        // A clickable picker of executables we've used before plus any found in the content, so the
        // user can choose the game program (or a setup tool) instead of hunting through DOS.
        var executables = OrderedExecutables(
            services.Store.ReadState(tile.Game.GameboxPath),
            ScanExecutables(Path.Combine(tile.Game.GameboxPath, "content")));
        // Also offer programs a CD installer put on the persisted C: drive (dosbox_pure's *.pure.zip
        // save), which the content-folder scan can't see — so installed CD games get a working picker.
        foreach (var installed in PureSave.ListInstalledExecutables(Path.Combine(tile.Game.GameboxPath, "saves")))
            if (!executables.Contains(installed, StringComparer.OrdinalIgnoreCase))
                executables.Add(installed);
        if (executables.Count > 0)
        {
            var choose = new MenuItem { Header = "📄  Choose program…" };
            choose.Click += (_, _) => ChooseProgram(tile, executables);
            menu.Items.Add(choose);
        }

        // For disc-based games (e.g. an installed Windows machine), let the user add more discs —
        // each shows up in dosbox_pure's start menu to mount as D: (swap CDs without leaving the OS).
        if (IsDiscGame(tile))
        {
            var addDisc = new MenuItem { Header = "💿  Add disc…" };
            addDisc.Click += (_, _) => AddDisc(tile);
            menu.Items.Add(addDisc);

            var addFromFolder = new MenuItem { Header = "💿  Add disc from folder…" };
            addFromFolder.Click += (_, _) => AddDiscFromFolder(tile);
            menu.Items.Add(addFromFolder);
        }

        menu.Items.Add(new Separator());
        menu.Items.Add(boxArt);
        menu.Items.Add(box3D);
        menu.Items.Add(customArt);
        menu.Items.Add(boxStyle);
        menu.Items.Add(rename);
        menu.Items.Add(manual);

        // ── Destructive (last) ──
        menu.Items.Add(new Separator());
        var delete = new MenuItem { Header = "🗑  Delete" };
        delete.Click += (_, _) =>
        {
            if (Vm is null)
                return;
            var ok = MessageBox.Show(this,
                $"Delete \"{tile.Title}\"?\n\nThis removes the game and its files — saves, save states and notes. " +
                "Downloaded artwork is kept, so re-adding the game restores it. This can't be undone.",
                "Delete game", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
            if (ok == MessageBoxResult.OK)
                Vm.DeleteGames(new[] { tile });
        };
        menu.Items.Add(delete);

        menu.IsOpen = true;
        e.Handled = true;
    }

    // Edit the command-line arguments passed to the game's program on launch (persisted to the
    // profile). Some games need a sound/mode switch and have no SETUP to do it.
    private async void RenameFromScreenScraper(GameTile tile)
    {
        var dialog = new TextPromptDialog(
            $"Rename from ScreenScraper — {tile.Title}",
            "Type the game's name to look up. It'll be renamed to ScreenScraper's matched title and its " +
            "art and details refreshed. Use the exact title (e.g. \"King's Quest VI\") for ones the " +
            "automatic match can't find.",
            tile.Title) { Owner = this };
        if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.Value))
            return;
        if (Vm is not null)
            await Vm.RenameFromScreenScraperAsync(tile, dialog.Value.Trim());
    }

    private void EditLaunchParameters(GameTile tile)
    {
        var services = ((App)Application.Current).Services;
        var profile = services.Store.ReadProfile(tile.Game.GameboxPath);

        var dialog = new TextPromptDialog(
            $"Launch parameters — {tile.Title}",
            "Command-line arguments passed to the game's program when it starts (e.g. a sound-mode switch some games need). Leave blank for none.",
            profile.Launch.Arguments) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        var args = string.IsNullOrWhiteSpace(dialog.Value) ? null : dialog.Value;
        var updated = profile with { Launch = profile.Launch with { Arguments = args } };
        services.Store.WriteProfile(tile.Game.GameboxPath, updated);
        Vm?.Report(
            args is null ? $"Cleared launch parameters for {tile.Title}." : $"Launch parameters set: {args}",
            busy: false);
    }

    private async void ChooseProgram(GameTile tile, List<string> executables)
    {
        var services = ((App)Application.Current).Services;
        var state = services.Store.ReadState(tile.Game.GameboxPath);
        // Pre-select what we'd launch by default: the remembered exe, else the smart-detected game.
        var current = string.IsNullOrWhiteSpace(state.LastExecutable)
            ? BestGameExecutable(Path.Combine(tile.Game.GameboxPath, "content"), tile.Title)
            : state.LastExecutable;

        var dialog = new ChooseProgramDialog(tile.Title, executables, current) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.SelectedExecutable is not { } exe)
            return;

        if (PureSave.IsInstalledPath(exe))
        {
            // A program installed on the persisted C: drive — pin it via AUTOBOOT.DBP so dosbox_pure
            // boots straight into it with the disc still mounted as D: (CD checks / CD audio keep
            // working), then launch. dosbox_pure reads AUTOBOOT.DBP from C: at startup.
            PureSave.SetAutoBoot(Path.Combine(tile.Game.GameboxPath, "saves"), exe);
            await LaunchGameAsync(tile);
        }
        else
        {
            await LaunchGameAsync(tile, executableOverride: exe); // remembers it (unless it's a setup tool)
        }
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

    /// <summary>The most likely game program among the content's executables — a name matching the
    /// title, then a .bat launcher when a separate DOS extender is present, then the largest exe (the
    /// game engine dwarfs config/registration helpers) — skipping installers and setup tools.</summary>
    private static string? BestGameExecutable(string contentDir, string title)
    {
        var pick = BestGameExecutableCore(contentDir, title);
        // Follow a hardcoded-path launcher .bat to the real exe, so packaged games whose .bat assumes
        // a fixed install path still launch in one click.
        return pick is null ? null : DosExecutables.ResolveBatRedirect(contentDir, pick);
    }

    private static string? BestGameExecutableCore(string contentDir, string title)
    {
        var candidates = ScanExecutables(contentDir)
            .Where(e => !IsSetupLike(e) && !DosExecutables.IsRuntimeHelper(e))
            .ToList();
        if (candidates.Count == 0)
            return null;

        // 1. Name matches the title — exact word, substring, initials, or first-word prefix
        //    (handles abbreviated/truncated exe names). See DosExecutables.TitleMatches.
        var titled = candidates.FirstOrDefault(e => DosExecutables.TitleMatches(e, title));
        if (titled is not null)
            return titled;

        // 2. A canonical launcher (SIERRA.EXE, RUN.BAT, …). Sierra SCI games have no title-named
        //    exe, so without this they fall to the largest exe — which can be a big utility like a
        //    DVD-prep wizard rather than the game's own SIERRA.EXE interpreter.
        var known = candidates.FirstOrDefault(DosExecutables.IsKnownLauncher);
        if (known is not null)
            return known;

        // 3. The largest executable — the game engine (often 1+ MB) dwarfs the tens-of-KB
        //    config/registration/splash helpers. This is the first-launch guess; once the user
        //    runs the real program from DOS, that capture takes over.
        //    Bundled utilities (a DVD-prep wizard, a patcher) are ranked last so a big tool never
        //    outweighs the actual game.
        static long Size(string p) { try { return new FileInfo(p).Length; } catch { return 0; } }
        var best = candidates
            .Select(e => (exe: e, size: Size(Path.Combine(contentDir, e)), util: DosExecutables.IsLikelyUtility(e)))
            .OrderBy(x => x.util)
            .ThenByDescending(x => x.size)
            .FirstOrDefault();
        return best.size > 0
            ? best.exe
            : (candidates.FirstOrDefault(e => e.Contains('\\')) ?? candidates[0]);
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
            Title = $"Add disc(s) to {tile.Title}",
            Filter = "Disc images|*.iso;*.cue;*.bin;*.chd|All files|*.*",
            Multiselect = true,
        };
        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            var content = Path.Combine(tile.Game.GameboxPath, "content");
            Directory.CreateDirectory(content);

            // A .bin is mounted via its .cue, so don't double-copy a .bin that a selected .cue brings.
            var cueBins = dialog.FileNames
                .Where(f => f.EndsWith(".cue", StringComparison.OrdinalIgnoreCase))
                .SelectMany(cue => Core.Import.ImportPipeline.CueReferencedFiles(cue).Select(Path.GetFileName))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            int added = 0;
            var udf = new List<string>();
            foreach (var path in dialog.FileNames)
            {
                if (path.EndsWith(".bin", StringComparison.OrdinalIgnoreCase) && cueBins.Contains(Path.GetFileName(path)))
                    continue; // its .cue will copy it

                CopyDiscWithSidecars(path, content);
                added++;
                if (path.EndsWith(".iso", StringComparison.OrdinalIgnoreCase) && !Core.Import.ImportPipeline.IsIso9660(path))
                    udf.Add(Path.GetFileNameWithoutExtension(path));
            }

            var msg = $"Added {added} disc{(added == 1 ? "" : "s")} to {tile.Title} — launch it and swap discs from the start menu.";
            if (udf.Count > 0)
                msg += $"  ⚠ {string.Join(", ", udf)} isn't a standard ISO9660 CD (e.g. UDF) and likely won't mount.";
            Vm?.Report(msg, busy: false);
        }
        catch (Exception ex)
        {
            Vm?.Report($"Couldn't add disc: {ex.Message}", busy: false);
        }
    }

    // Copy a disc image into the box; for a .cue, bring the track files it references along.
    private static void CopyDiscWithSidecars(string source, string content)
    {
        File.Copy(source, Path.Combine(content, Path.GetFileName(source)), overwrite: true);
        if (!source.EndsWith(".cue", StringComparison.OrdinalIgnoreCase))
            return;
        var sourceDir = Path.GetDirectoryName(source) ?? string.Empty;
        foreach (var track in Core.Import.ImportPipeline.CueReferencedFiles(source))
        {
            var trackPath = Path.Combine(sourceDir, track);
            if (File.Exists(trackPath))
                File.Copy(trackPath, Path.Combine(content, Path.GetFileName(track)), overwrite: true);
        }
    }

    // Build an ISO9660 image from a folder (e.g. files extracted from a rip the emulator can't read)
    // and attach it as a disc. Useful for getting loose game files onto a disc a guest OS can mount.
    private async void AddDiscFromFolder(GameTile tile)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = $"Pick a folder to turn into a disc for {tile.Title}",
        };
        if (dialog.ShowDialog(this) != true)
            return;

        var folder = dialog.FolderName;
        var name = SafeDiscName(Path.GetFileName(folder.TrimEnd('\\', '/')));
        var content = Path.Combine(tile.Game.GameboxPath, "content");
        Directory.CreateDirectory(content);
        var isoPath = Path.Combine(content, name + ".iso");

        Vm?.Report($"Building a disc image from {Path.GetFileName(folder)}… this can take a minute.", busy: true);
        try
        {
            await BuildIsoOnStaThread(folder, isoPath, name);
            Vm?.Report($"Added {name}.iso to {tile.Title} — launch it and INSERT the disc from the menu (F10).", busy: false);
        }
        catch (Exception ex)
        {
            try { if (File.Exists(isoPath)) File.Delete(isoPath); } catch { /* leave a locked partial */ }
            Vm?.Report($"Couldn't build the disc image: {ex.Message}", busy: false);
        }
    }

    private static string SafeDiscName(string raw)
    {
        var cleaned = new string(raw.Where(c => !Path.GetInvalidFileNameChars().Contains(c)).ToArray()).Trim();
        return cleaned.Length == 0 ? "disc" : cleaned;
    }

    // IMAPI2 is COM and wants an STA thread; this also keeps the slow build off the UI thread.
    private static Task BuildIsoOnStaThread(string folder, string isoPath, string label)
    {
        var tcs = new TaskCompletionSource();
        var thread = new System.Threading.Thread(() =>
        {
            try
            {
                Core.Import.IsoBuilder.BuildFromFolder(folder, isoPath, label);
                tcs.SetResult();
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        })
        {
            IsBackground = true,
        };
        thread.SetApartmentState(System.Threading.ApartmentState.STA);
        thread.Start();
        return tcs.Task;
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
        Vm?.ReapplyBoxStyle(); // pick up a changed "Use 3D boxes by default"
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

    private void OnBoxMouseUp(object sender, MouseButtonEventArgs e)
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

        // Ctrl+click toggles selection (for delete); a plain click opens the game card.
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            tile.IsSelected = !tile.IsSelected;
            e.Handled = true;
            return;
        }

        Vm?.ClearSelection();
        OpenGameCard(tile);
    }

    private GameDetailWindow? _openCard;

    /// <summary>Open the per-game detail card (Play launches; ★ favorites; "…" has the rest).</summary>
    private void OpenGameCard(GameTile tile)
    {
        _openCard?.Close();
        var services = ((App)Application.Current).Services;

        var overflow = new List<(string, Action)>
        {
            ("Manage…", () => OpenManage(tile)),
            ("Rename from ScreenScraper…", () => RenameFromScreenScraper(tile)),
            ("Cheats… (preview)", () => new CheatWindow { Owner = this }.Show()),
            ("Game preferences…", () => OpenOptions(tile)),
            ("Open in DOS", () => _ = LaunchGameAsync(tile, bootToDos: true)),
            ("Launch parameters…", () => EditLaunchParameters(tile)),
            ("Download manual", () => _ = DownloadManualAsync(tile)),
        };

        var executables = OrderedExecutables(
            services.Store.ReadState(tile.Game.GameboxPath),
            ScanExecutables(Path.Combine(tile.Game.GameboxPath, "content")));
        foreach (var installed in PureSave.ListInstalledExecutables(Path.Combine(tile.Game.GameboxPath, "saves")))
            if (!executables.Contains(installed, StringComparer.OrdinalIgnoreCase))
                executables.Add(installed);
        if (executables.Count > 0)
            overflow.Add(("Choose program…", () => ChooseProgram(tile, executables)));

        _openCard = new GameDetailWindow(tile, services, () => _ = LaunchGameAsync(tile), overflow) { Owner = this };
        _openCard.Closed += (s, _) => { if (ReferenceEquals(_openCard, s)) _openCard = null; };
        _openCard.Show();
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

    // Open the per-game Manage window; if the user picks a save state to load, launch the game
    // restored to it once the (modal) window closes.
    private void OpenManage(GameTile tile)
    {
        var w = new ManageGameWindow(((App)Application.Current).Services, tile.Game) { Owner = this };
        w.ShowDialog();
        if (w.StateToLaunch is { } state && Core.Library.SaveStateStore.ReadState(state) is { } bytes)
            _ = LaunchGameAsync(tile, loadState: bytes);
    }

    private async Task LaunchGameAsync(GameTile tile, bool bootToDos = false, string? executableOverride = null,
                                       byte[]? loadState = null)
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
            // Pick the executable. Order: an explicit Run/picker choice this launch, then a program
            // the user *deliberately* picked before, then auto-detect the game (title / extender-
            // launcher .bat / largest exe), then the configured guess. The import/curated guess is
            // often a config tool (COMMIT/SETMAIN) or extender (DOS4GW), so it ranks last — and a
            // stale auto-value never wins because only the picker sets ExecutableIsUserChoice.
            var state = services.Store.ReadState(tile.Game.GameboxPath);
            var configured = instance.Profile.Launch.Executable;
            var chosen = executableOverride
                ?? (state.ExecutableIsUserChoice ? state.LastExecutable : null)
                ?? state.LastRunProgram
                ?? BestGameExecutable(Path.Combine(tile.Game.GameboxPath, "content"), tile.Title)
                ?? configured;

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

        // Folder games write in-game saves into content/; snapshot a baseline (if none yet) before
        // play so the Manage window and sync can tell saves from the original game files.
        if (instance.Profile.SourceMedia != EmuDOS.Core.Model.SourceMediaType.Iso)
            EmuDOS.Core.Library.ContentBaseline.CaptureIfMissing(instance.ContentPath, instance.SavePath);

        var engine = new DosBoxPureEngine(
            services.Downloads.InstalledPath(AssetManifest.DosBoxPure), services.Paths.SystemDir);
        services.Library.RecordPlay(tile.Id);

        new EmulatorWindow(engine, instance, tile.Id, loadState) { Owner = this }.Show();
        Vm.ClearStatus();
    }
}
