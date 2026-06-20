using System.Windows;
using System.Windows.Input;

namespace EmuDOS;

/// <summary>A clickable picker of the programs found in a game's content — the game exe, an
/// installer, a setup tool — so the user can choose what to launch instead of dropping to DOS.</summary>
public partial class ChooseProgramDialog : Window
{
    public string? SelectedExecutable { get; private set; }

    public ChooseProgramDialog(string title, IEnumerable<string> executables, string? current)
    {
        InitializeComponent();
        TitleText.Text = $"Run a program — {title}";

        foreach (var exe in executables)
        {
            var item = new ExeItem(exe, IsSetupLike(exe));
            ExeList.Items.Add(item);
            if (current is not null && string.Equals(exe, current, StringComparison.OrdinalIgnoreCase))
                ExeList.SelectedItem = item;
        }

        if (ExeList.SelectedItem is null && ExeList.Items.Count > 0)
            ExeList.SelectedIndex = 0;
    }

    private void OnListDoubleClick(object sender, MouseButtonEventArgs e) => Run();
    private void OnRunClick(object sender, RoutedEventArgs e) => Run();
    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Run()
    {
        if (ExeList.SelectedItem is ExeItem item)
        {
            SelectedExecutable = item.Path;
            DialogResult = true;
        }
    }

    private static bool IsSetupLike(string executable)
    {
        var name = System.IO.Path.GetFileNameWithoutExtension(executable).ToLowerInvariant();
        return name.Contains("setup") || name.Contains("install") || name.Contains("config");
    }

    private sealed record ExeItem(string Path, bool IsSetup)
    {
        public override string ToString() => IsSetup ? $"{Path}   —  setup tool" : Path;
    }
}
