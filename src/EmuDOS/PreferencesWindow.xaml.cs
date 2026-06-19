using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
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

        DownloadList.ItemsSource = AssetManifest.All
            .Select(a => new DownloadRow(a, _services.Downloads.IsInstalled(a)))
            .ToList();

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
        OnDisplaySliderChanged(this, default);

        GameOptionsStatus.Text = string.Empty;
    }

    private void OnDisplaySliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
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

    private async void OnLoginScreenScraper(object sender, RoutedEventArgs e)
    {
        SsLogin.IsEnabled = false;
        Set(SsStatus, "Testing…", Pending);

        _services.Settings.ScreenScraperUser = SsUser.Text.Trim();
        _services.Settings.ScreenScraperPassword = SsPass.Password;
        _services.SettingsStore.Save(_services.Settings);

        bool ok = await _services.ValidateScreenScraperAsync(
            _services.Settings.ScreenScraperUser, _services.Settings.ScreenScraperPassword);
        if (ok)
        {
            _services.ReloadArtService();
            TriggerArtRefetch();
        }

        Set(SsStatus, ok ? $"✓ Logged in as {_services.Settings.ScreenScraperUser}" : "✗ Login failed",
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

    private static void Set(TextBlock target, string text, Brush brush)
    {
        target.Text = text;
        target.Foreground = brush;
    }
}
