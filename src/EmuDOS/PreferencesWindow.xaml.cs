using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using EmuDOS.Services;

namespace EmuDOS;

/// <summary>The tabbed preferences/management window. First tab: Snaps (art accounts).</summary>
public partial class PreferencesWindow : Window
{
    private static readonly Brush Pending = new SolidColorBrush(Color.FromRgb(0xA8, 0x9A, 0x86));
    private static readonly Brush Success = new SolidColorBrush(Color.FromRgb(0x9F, 0xE0, 0xA0));
    private static readonly Brush Failure = new SolidColorBrush(Color.FromRgb(0xE0, 0x85, 0x85));

    private readonly AppServices _services;

    public PreferencesWindow(AppServices services)
    {
        InitializeComponent();
        _services = services;

        SsUser.Text = services.Settings.ScreenScraperUser;
        SsPass.Password = services.Settings.ScreenScraperPassword;
        SgdbKey.Text = services.Settings.SteamGridDbKey;
    }

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

    private static void Set(TextBlock target, string text, Brush brush)
    {
        target.Text = text;
        target.Foreground = brush;
    }
}
