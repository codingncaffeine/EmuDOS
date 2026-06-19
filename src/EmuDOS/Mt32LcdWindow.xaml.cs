using System.Windows;
using System.Windows.Input;

namespace EmuDOS;

/// <summary>A floating Roland MT-32 LCD: shows the 20-char display text the game sends.</summary>
public partial class Mt32LcdWindow : Window
{
    public Mt32LcdWindow()
    {
        InitializeComponent();
    }

    public void SetText(string text) => Lcd.Text = text ?? string.Empty;

    private void OnDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    // Scroll wheel resizes the whole unit; the Viewbox keeps the aspect ratio and the window
    // auto-sizes to the scaled content.
    private void OnScaleWheel(object sender, MouseWheelEventArgs e)
    {
        Scaler.Width = Math.Clamp(Scaler.Width + (e.Delta > 0 ? 60 : -60), 320, 1184);
        e.Handled = true;
    }
}
