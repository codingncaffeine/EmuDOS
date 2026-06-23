using System.Windows;
using System.Windows.Input;

namespace EmuDOS;

/// <summary>
/// The cheat engine's window — a System-Shock-style CRT diagnostic console. Currently a styled shell
/// with mock data; the scan/edit/freeze engine (over LibretroCore.MemoryRegions) wires in behind it.
/// </summary>
public partial class CheatWindow : Window
{
    public CheatWindow() => InitializeComponent();

    private void OnTitleDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void OnClose(object sender, MouseButtonEventArgs e) => Close();

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
            Close();
    }
}
