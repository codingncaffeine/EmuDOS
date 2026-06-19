using System.Windows;
using System.Windows.Input;
using EmuDOS.Core.Downloads;
using EmuDOS.Core.Engine.DosBoxPure;
using EmuDOS.ViewModels;

namespace EmuDOS;

/// <summary>Interaction logic for MainWindow.xaml.</summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private MainViewModel? Vm => DataContext as MainViewModel;

    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (Vm is not null && e.Data.GetData(DataFormats.FileDrop) is string[] paths && paths.Length > 0)
            await Vm.ImportPathsAsync(paths);
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnGameClick(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is GameTile tile)
            await LaunchGameAsync(tile);
    }

    /// <summary>Launch the first game on the shelf (used by the auto-play dev hook).</summary>
    public async Task PlayFirstAsync()
    {
        if (Vm?.Games.Count > 0)
            await LaunchGameAsync(Vm.Games[0]);
    }

    private async Task LaunchGameAsync(GameTile tile)
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
        var engine = new DosBoxPureEngine(
            services.Downloads.InstalledPath(AssetManifest.DosBoxPure), services.Paths.SystemDir);
        services.Library.RecordPlay(tile.Id);

        new EmulatorWindow(engine, instance) { Owner = this }.Show();
        Vm.ClearStatus();
    }
}
