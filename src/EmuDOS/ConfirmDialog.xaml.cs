using System.Windows;

namespace EmuDOS;

/// <summary>A small themed yes/no confirmation dialog (replaces the Win32 MessageBox).</summary>
public partial class ConfirmDialog : Window
{
    public ConfirmDialog()
    {
        InitializeComponent();
    }

    /// <summary>Show the dialog modally; true if the user confirmed.</summary>
    public static bool Show(Window owner, string title, string message, string confirmText = "OK")
    {
        var dialog = new ConfirmDialog { Owner = owner };
        dialog.TitleText.Text = title;
        dialog.MessageText.Text = message;
        dialog.ConfirmBtn.Content = confirmText;
        return dialog.ShowDialog() == true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;

    private void OnConfirm(object sender, RoutedEventArgs e) => DialogResult = true;
}
