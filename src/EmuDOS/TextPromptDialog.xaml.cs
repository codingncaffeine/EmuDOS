using System.Windows;
using System.Windows.Input;

namespace EmuDOS;

/// <summary>A minimal single-line text prompt (title, description, input + Save/Cancel).</summary>
public partial class TextPromptDialog : Window
{
    public string Value { get; private set; } = string.Empty;

    public TextPromptDialog(string title, string description, string? current)
    {
        InitializeComponent();
        DarkChrome.Apply(this);
        TitleText.Text = title;
        DescText.Text = description;
        InputBox.Text = current ?? string.Empty;
        Loaded += (_, _) => { InputBox.Focus(); InputBox.SelectAll(); };
    }

    private void OnInputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) Accept();
        else if (e.Key == Key.Escape) DialogResult = false;
    }

    private void OnOk(object sender, RoutedEventArgs e) => Accept();

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Accept()
    {
        Value = InputBox.Text.Trim();
        DialogResult = true;
    }
}
