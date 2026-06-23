using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using EmuDOS.Core.Downloads;
using EmuDOS.Core.Model;
using EmuDOS.Services;
using EmuDOS.ViewModels;

namespace EmuDOS;

/// <summary>
/// Tabbed preferences. "Game Options" edits one game's curated DOSBox settings (saved as a
/// per-game override); "Snaps" holds the art-source accounts.
/// </summary>
public partial class PreferencesWindow : Window
{
    private static readonly Brush Pending = new SolidColorBrush(Color.FromRgb(0xA8, 0x9A, 0x86));
    private static readonly Brush Success = new SolidColorBrush(Color.FromRgb(0x9F, 0xE0, 0xA0));
    private static readonly Brush Failure = new SolidColorBrush(Color.FromRgb(0xE0, 0x85, 0x85));

    private static readonly int[] MemoryPresets = [4, 8, 16, 32, 64];

    private readonly AppServices _services;
    private readonly GameTile? _game;
    private GameProfile? _profile;

    public PreferencesWindow(AppServices services, GameTile? game = null)
    {
        InitializeComponent();
        DarkChrome.Apply(this);
        _services = services;
        _game = game;

        SsUser.Text = services.Settings.ScreenScraperUser;
        SsPass.Password = services.Settings.ScreenScraperPassword;
        SgdbKey.Text = services.Settings.SteamGridDbKey;
        Use3DBox.IsChecked = services.Settings.Use3DBoxes;

        ScreenshotFolderBox.Text = string.IsNullOrWhiteSpace(services.Settings.ScreenshotFolder)
            ? services.Paths.ScreenshotsDir : services.Settings.ScreenshotFolder;
        VideoFolderBox.Text = string.IsNullOrWhiteSpace(services.Settings.VideoFolder)
            ? services.Paths.VideosDir : services.Settings.VideoFolder;
        ScreenshotSizeBox.SelectedIndex = services.Settings.ScreenshotOriginalSize ? 0 : 1;
        VideoQualityBox.SelectedIndex = services.Settings.VideoQuality switch { "Low" => 0, "High" => 2, _ => 1 };

        HotkeyScreenshot.Text = Display(services.Settings.ScreenshotKey, "F12");
        HotkeyRecord.Text = Display(services.Settings.RecordKey, "F9");
        HotkeyMouseLock.Text = Display(services.Settings.MouseLockKey, "Middle Mouse");
        HotkeyMenu.Text = Display(services.Settings.MenuKey, "F10");
        HotkeySaveState.Text = Display(services.Settings.SaveStateKey, "F5");
        HotkeyLoadState.Text = Display(services.Settings.LoadStateKey, "F8");
        HotkeyCheat.Text = Display(services.Settings.CheatKey, "F11");

        UpdateCloudUi();

        DownloadList.ItemsSource = AssetManifest.All
            .Select(a => new DownloadRow(a, _services.Downloads.IsInstalled(a)))
            .ToList();

        bool hasRoms = _services.SystemFiles.HasMt32;
        Set(Mt32RomStatus,
            hasRoms ? "✓ MT-32 ROMs detected" : "✗ Not found — drag the ROMs in to enable MT-32 audio",
            hasRoms ? Success : Pending);

        if (game is null)
        {
            GameOptionsTab.Visibility = Visibility.Collapsed;
            Tabs.SelectedIndex = 1; // Snaps
        }
        else
        {
            _profile = _services.Store.ReadProfile(game.Game.GameboxPath);
            PopulateGameOptions();
            Tabs.SelectedItem = GameOptionsTab;
        }
    }

    // ── Game Options ────────────────────────────────────────────────────────────

    private void PopulateGameOptions()
    {
        if (_profile is null)
            return;

        GameTitle.Text = _profile.Title;

        CpuCyclesMode.ItemsSource = Enum.GetValues<CyclesMode>();
        CpuCyclesMode.SelectedItem = _profile.Cpu.CyclesMode;
        FixedCycles.Text = _profile.Cpu.FixedCycles > 0 ? _profile.Cpu.FixedCycles.ToString() : "60000";
        UpdateCyclesEnabled();

        MachineTypeBox.ItemsSource = Enum.GetValues<MachineType>();
        MachineTypeBox.SelectedItem = _profile.Machine.Machine;

        MemoryBox.ItemsSource = MemoryPresets.Contains(_profile.Memory.SizeMb)
            ? MemoryPresets
            : MemoryPresets.Append(_profile.Memory.SizeMb).OrderBy(x => x).ToArray();
        MemoryBox.SelectedItem = _profile.Memory.SizeMb;

        SoundCardBox.ItemsSource = Enum.GetValues<SoundBlasterType>();
        SoundCardBox.SelectedItem = _profile.Sound.SoundBlaster;

        MidiBox.ItemsSource = Enum.GetValues<MidiDevice>();
        MidiBox.SelectedItem = _profile.Sound.Midi;

        AspectBox.IsChecked = _profile.Machine.AspectCorrection;

        BrightnessSlider.Value = _profile.Display.Brightness;
        GammaSlider.Value = _profile.Display.Gamma;
        UpdateDisplayValues();

        GameOptionsStatus.Text = string.Empty;
    }

    private void OnDisplaySliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e) =>
        UpdateDisplayValues();

    private void UpdateDisplayValues()
    {
        if (BrightnessValue is not null)
            BrightnessValue.Text = BrightnessSlider.Value.ToString("0.00");
        if (GammaValue is not null)
            GammaValue.Text = GammaSlider.Value.ToString("0.00");
    }

    private void OnCyclesModeChanged(object sender, SelectionChangedEventArgs e) => UpdateCyclesEnabled();

    private void UpdateCyclesEnabled()
    {
        bool fixedMode = CpuCyclesMode.SelectedItem is CyclesMode.Fixed;
        if (FixedCycles is not null)
            FixedCycles.IsEnabled = fixedMode;
        if (CyclesHint is not null)
            CyclesHint.Visibility = fixedMode ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnSaveGameOptions(object sender, RoutedEventArgs e)
    {
        if (_game is null || _profile is null)
            return;

        int cycles = int.TryParse(FixedCycles.Text, out var c) && c > 0 ? c : _profile.Cpu.FixedCycles;

        var updated = _profile with
        {
            Cpu = _profile.Cpu with
            {
                CyclesMode = (CyclesMode)CpuCyclesMode.SelectedItem!,
                FixedCycles = cycles,
            },
            Machine = _profile.Machine with
            {
                Machine = (MachineType)MachineTypeBox.SelectedItem!,
                AspectCorrection = AspectBox.IsChecked == true,
            },
            Memory = _profile.Memory with { SizeMb = (int)MemoryBox.SelectedItem! },
            Sound = _profile.Sound with
            {
                SoundBlaster = (SoundBlasterType)SoundCardBox.SelectedItem!,
                Midi = (MidiDevice)MidiBox.SelectedItem!,
            },
            Display = _profile.Display with
            {
                Brightness = BrightnessSlider.Value,
                Gamma = GammaSlider.Value,
            },
            Origin = ProfileOrigin.UserOverride,
        };

        _services.Store.WriteProfile(_game.Game.GameboxPath, updated);
        _profile = updated;
        GameOptionsStatus.Foreground = Success;
        GameOptionsStatus.Text = "Saved — applies next launch.";
    }

    private void OnResetGameOptions(object sender, RoutedEventArgs e)
    {
        if (_game is null || _profile is null)
            return;

        var contentDir = _services.Store.Resolve(_game.Game.GameboxPath).ContentPath;
        var names = Directory.Exists(contentDir)
            ? Directory.EnumerateFiles(contentDir).Select(Path.GetFileName).OfType<string>()
            : Enumerable.Empty<string>();

        var baseline = _profile with { Origin = ProfileOrigin.Default };
        var resolved = _services.Resolver.Resolve(baseline, names);
        _services.Store.WriteProfile(_game.Game.GameboxPath, resolved);
        _profile = resolved;

        PopulateGameOptions();
        GameOptionsStatus.Foreground = Success;
        GameOptionsStatus.Text = "Reset to catalog default.";
    }

    // ── Snaps (art accounts) ──────────────────────────────────────────────────────

    private static string Display(string value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value;

    private void OnHotkeyCapture(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox box)
            return;
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Escape)
        {
            box.Text = box == HotkeyMouseLock ? "Middle Mouse"
                : box == HotkeyRecord ? "F9"
                : box == HotkeyMenu ? "F10"
                : box == HotkeySaveState ? "F5"
                : box == HotkeyLoadState ? "F8"
                : "F12";
            return;
        }
        box.Text = key.ToString();
    }

    private void OnSaveHotkeys(object sender, RoutedEventArgs e)
    {
        _services.Settings.ScreenshotKey = HotkeyScreenshot.Text.Trim();
        _services.Settings.RecordKey = HotkeyRecord.Text.Trim();
        _services.Settings.MouseLockKey = HotkeyMouseLock.Text == "Middle Mouse" ? string.Empty : HotkeyMouseLock.Text.Trim();
        _services.Settings.MenuKey = HotkeyMenu.Text.Trim();
        _services.Settings.SaveStateKey = HotkeySaveState.Text.Trim();
        _services.Settings.LoadStateKey = HotkeyLoadState.Text.Trim();
        _services.Settings.CheatKey = HotkeyCheat.Text.Trim();
        _services.SettingsStore.Save(_services.Settings);
        Set(HotkeysStatus, "Saved — applies next launch.", Success);
    }

    private void OnBrowseScreenshotFolder(object sender, RoutedEventArgs e) => BrowseInto(ScreenshotFolderBox);
    private void OnBrowseVideoFolder(object sender, RoutedEventArgs e) => BrowseInto(VideoFolderBox);

    private static void BrowseInto(TextBox target)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Choose a folder" };
        if (!string.IsNullOrWhiteSpace(target.Text) && Directory.Exists(target.Text))
            dialog.InitialDirectory = target.Text;
        if (dialog.ShowDialog() == true)
            target.Text = dialog.FolderName;
    }

    private void OnSaveMedia(object sender, RoutedEventArgs e)
    {
        _services.Settings.ScreenshotFolder = ScreenshotFolderBox.Text.Trim();
        _services.Settings.VideoFolder = VideoFolderBox.Text.Trim();
        _services.Settings.ScreenshotOriginalSize = ScreenshotSizeBox.SelectedIndex == 0;
        _services.Settings.VideoQuality = VideoQualityBox.SelectedIndex switch { 0 => "Low", 2 => "High", _ => "Medium" };
        _services.SettingsStore.Save(_services.Settings);
        Set(MediaStatus, "Saved.", Success);
    }

    private void OnToggle3DBoxes(object sender, RoutedEventArgs e)
    {
        _services.Settings.Use3DBoxes = Use3DBox.IsChecked == true;
        _services.SettingsStore.Save(_services.Settings);
        // The shelf re-applies this when the dialog closes (MainWindow re-reads the setting).
    }

    private async void OnLoginScreenScraper(object sender, RoutedEventArgs e)
    {
        SsLogin.IsEnabled = false;
        Set(SsStatus, "Testing…", Pending);

        _services.Settings.ScreenScraperUser = SsUser.Text.Trim();
        _services.Settings.ScreenScraperPassword = SsPass.Password;
        _services.SettingsStore.Save(_services.Settings);

        var (ok, maxThreads) = await _services.ValidateScreenScraperAsync(
            _services.Settings.ScreenScraperUser, _services.Settings.ScreenScraperPassword);
        if (ok)
        {
            // Remember the account's allowed concurrency so bulk art downloads can parallelise.
            _services.Settings.ScreenScraperMaxThreads = maxThreads;
            _services.SettingsStore.Save(_services.Settings);
            _services.ReloadArtService();
            TriggerArtRefetch();
        }

        Set(SsStatus,
            ok ? $"✓ Logged in as {_services.Settings.ScreenScraperUser} ({maxThreads} thread{(maxThreads == 1 ? "" : "s")})"
               : "✗ Login failed",
            ok ? Success : Failure);
        SsLogin.IsEnabled = true;
    }

    private async void OnLoginSteamGridDb(object sender, RoutedEventArgs e)
    {
        SgdbLogin.IsEnabled = false;
        Set(SgdbStatus, "Testing…", Pending);

        _services.Settings.SteamGridDbKey = SgdbKey.Text.Trim();
        _services.SettingsStore.Save(_services.Settings);

        bool ok = await _services.ValidateSteamGridDbAsync(_services.Settings.SteamGridDbKey);
        if (ok)
        {
            _services.ReloadArtService();
            TriggerArtRefetch();
        }

        Set(SgdbStatus, ok ? "✓ Key valid — fetching missing covers…" : "✗ Invalid key", ok ? Success : Failure);
        SgdbLogin.IsEnabled = true;
    }

    private void TriggerArtRefetch()
    {
        if (Owner is MainWindow main)
            _ = main.RefetchMissingArtAsync();
    }

    // ── Downloads ─────────────────────────────────────────────────────────────────

    private async void OnDownloadClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not DownloadRow row)
            return;

        row.IsBusy = true;
        var progress = new Progress<DownloadProgress>(p =>
            row.SetProgress(p.Fraction is double f ? $"Downloading… {f:P0}" : "Downloading…"));

        var result = await _services.Downloads.DownloadAsync(row.Asset, progress);
        row.SetResult(result.Success, result.Error);
        row.IsBusy = false;
    }

    // ── Backups ─────────────────────────────────────────────────────────────────

    private void OnBackupDatabase(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Choose a folder for the database backup" };
        if (dlg.ShowDialog(this) != true)
            return;
        try
        {
            var src = Path.Combine(_services.Paths.DataRoot, "library.db");
            var dest = Path.Combine(dlg.FolderName, $"library-{System.DateTime.Now:yyyy-MM-dd-HHmm}.db");
            File.Copy(src, dest, overwrite: false);
            Set(BackupStatus, $"Database backed up to {dest}", Success);
        }
        catch (System.Exception ex) { Set(BackupStatus, $"Backup failed: {ex.Message}", Failure); }
    }

    private void OnRestoreDatabase(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choose a database backup",
            Filter = "Database (*.db)|*.db|All files (*.*)|*.*",
        };
        if (dlg.ShowDialog(this) != true)
            return;
        if (MessageBox.Show(this,
                "Restore this database on the next launch? It replaces your current favorites, play counts and history, and EmuDOS will need to restart.",
                "Restore database", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK)
            return;
        try
        {
            File.Copy(dlg.FileName, EmuDOS.Core.Library.LibraryDatabase.PendingRestorePath(_services.Paths), overwrite: true);
            Set(BackupStatus, "Restore staged — restart EmuDOS to apply it.", Success);
        }
        catch (System.Exception ex) { Set(BackupStatus, $"Restore failed: {ex.Message}", Failure); }
    }

    private void OnBackupAllSaves(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Choose where to save the backup archive" };
        if (dlg.ShowDialog(this) != true)
            return;
        try
        {
            var dest = Path.Combine(dlg.FolderName, $"emudos-saves-{System.DateTime.Now:yyyy-MM-dd-HHmm}.zip");
            int games = EmuDOS.Core.Library.SaveBackup.CreateAllSavesArchive(_services.Paths.GameboxesDir, dest);
            Set(BackupStatus, $"Backed up saves for {games} game(s) to {dest}", Success);
        }
        catch (System.Exception ex) { Set(BackupStatus, $"Backup failed: {ex.Message}", Failure); }
    }

    // ── Cloud sync ─────────────────────────────────────────────────────────────────
    private EmuDOS.Metadata.GitHubSyncService? _gh;
    private EmuDOS.Metadata.GitHubSyncService Gh => _gh ??= new EmuDOS.Metadata.GitHubSyncService(_services.CloudLog.Info);

    private void UpdateCloudUi()
    {
        var connected = !string.IsNullOrEmpty(_services.Settings.GitHubToken);
        CloudStatus.Text = connected ? $"Connected as {_services.Settings.GitHubLogin}." : "Not connected.";
        ConnectButton.Visibility = connected ? Visibility.Collapsed : Visibility.Visible;
        SyncNowButton.IsEnabled = connected;
        DisconnectButton.Visibility = connected ? Visibility.Visible : Visibility.Collapsed;
        CloudPassphrase.Text = _services.Settings.CloudEncryptionPassphrase;
    }

    // Persist the typed passphrase and derive the encryption key (null = no encryption).
    private byte[]? CloudKey()
    {
        var pass = CloudPassphrase.Text ?? string.Empty;
        if (_services.Settings.CloudEncryptionPassphrase != pass)
        {
            _services.Settings.CloudEncryptionPassphrase = pass;
            _services.SettingsStore.Save(_services.Settings);
        }
        return string.IsNullOrEmpty(pass) ? null : EmuDOS.Metadata.CloudCrypto.DeriveKey(pass);
    }

    private async void OnConnectGitHub(object sender, RoutedEventArgs e)
    {
        ConnectButton.IsEnabled = false;
        try
        {
            var code = await Gh.RequestDeviceCodeAsync();
            if (code is null)
            {
                CloudStatus.Text = "Couldn't start GitHub login. Check your connection.";
                return;
            }
            try { Clipboard.SetText(code.UserCode); } catch { /* clipboard may be busy */ }
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(code.VerificationUri) { UseShellExecute = true }); }
            catch { /* user can open it manually */ }
            CloudStatus.Text = $"Enter code  {code.UserCode}  at {code.VerificationUri} (copied to clipboard). Waiting for authorization…";

            var token = await Gh.PollAccessTokenAsync(code);
            if (token is null)
            {
                CloudStatus.Text = "Login timed out or was denied. Try again.";
                return;
            }
            _services.Settings.GitHubToken = token;
            _services.Settings.GitHubLogin = await Gh.GetLoginAsync(token) ?? "";
            _services.SettingsStore.Save(_services.Settings);
            _services.CloudLog.Info($"Connected as {_services.Settings.GitHubLogin}.");
            UpdateCloudUi();
        }
        catch (System.Exception ex)
        {
            CloudStatus.Text = $"Connect failed: {ex.Message}";
        }
        finally
        {
            ConnectButton.IsEnabled = true;
        }
    }

    private async void OnSyncNow(object sender, RoutedEventArgs e)
    {
        SyncNowButton.IsEnabled = false;
        var progress = new System.Progress<string>(s => CloudStatus.Text = s);
        try
        {
            var key = CloudKey();
            var s = _services.Settings;
            var result = await Gh.SyncAsync(s.GitHubToken, s.GitHubLogin, s.GitHubRepo,
                _services.Paths.GameboxesDir, Path.Combine(_services.Paths.DataRoot, "library.db"), progress, encKey: key);
            CloudStatus.Text = result.Ok
                ? $"Synced — {result.Uploaded} uploaded, {result.Downloaded} downloaded."
                : $"Sync failed: {result.Error}";
        }
        catch (System.Exception ex)
        {
            CloudStatus.Text = $"Sync failed: {ex.Message}";
        }
        finally
        {
            SyncNowButton.IsEnabled = true;
        }
    }

    private void OnDisconnect(object sender, RoutedEventArgs e)
    {
        _services.CloudLog.Info("Disconnected.");
        _services.Settings.GitHubToken = string.Empty;
        _services.Settings.GitHubLogin = string.Empty;
        _services.SettingsStore.Save(_services.Settings);
        UpdateCloudUi();
        CloudStatus.Text = "Disconnected.";
    }

    private static void Set(TextBlock target, string text, Brush brush)
    {
        target.Text = text;
        target.Foreground = brush;
    }
}
