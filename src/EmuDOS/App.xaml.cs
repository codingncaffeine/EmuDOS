using System.Windows;
using EmuDOS.Services;
using EmuDOS.ViewModels;

namespace EmuDOS;

/// <summary>Interaction logic for App.xaml.</summary>
public partial class App : Application
{
    public AppServices Services { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Services = new AppServices();
        new MainWindow { DataContext = new MainViewModel(Services) }.Show();
    }
}
