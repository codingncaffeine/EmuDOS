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
        CrashLog.Install(this); // record unhandled exceptions (incl. failures during startup below)
        UpdateService.CleanupOldFiles(); // sweep .old/.new left by a previous self-update
        Services = new AppServices();
        Core.Audio.Mt32Synth.RegisterNativeResolver(Services.Paths.CoresDir);
        var viewModel = new MainViewModel(Services);
        var window = new MainWindow { DataContext = viewModel };
        window.Show();
        UiFreezeWatchdog.Instance.Start(Dispatcher); // log UI stalls to ui_freezes.log

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

        // Check GitHub for a newer release and surface it in the bottom bar (best-effort, non-blocking).
        _ = viewModel.CheckForUpdatesAsync();

        // Backfill covers for anything already on the shelf without one.
        await viewModel.FetchMissingArtAsync();

        // Backfill descriptive metadata in the background (it's just text) so cards are pre-populated
        // and the user never waits on a fetch when opening one.
        _ = viewModel.FetchMissingMetadataAsync();
    }
}
