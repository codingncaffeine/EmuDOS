using System.Windows;
using EmuDOS.Services;
using EmuDOS.ViewModels;

namespace EmuDOS;

/// <summary>Interaction logic for App.xaml.</summary>
public partial class App : Application
{
    public AppServices Services { get; private set; } = null!;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Services = new AppServices();
        var viewModel = new MainViewModel(Services);
        var window = new MainWindow { DataContext = viewModel };
        window.Show();

        // Dev/smoke hooks (env-gated): import then auto-play a game on startup.
        var autoImport = Environment.GetEnvironmentVariable("EMUDOS_AUTOIMPORT");
        if (Environment.GetEnvironmentVariable("EMUDOS_DEV") == "1" || !string.IsNullOrWhiteSpace(autoImport))
        {
            window.Topmost = true;
            window.Activate();
            window.Topmost = false;
        }
        if (!string.IsNullOrWhiteSpace(autoImport))
            await viewModel.ImportPathsAsync(
                autoImport.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        if (Environment.GetEnvironmentVariable("EMUDOS_AUTOPLAY") == "1")
            await window.PlayFirstAsync();

        // Backfill covers for anything already on the shelf without one.
        await viewModel.FetchMissingArtAsync();
    }
}
