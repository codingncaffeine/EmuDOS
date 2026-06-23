using System;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace EmuDOS;

/// <summary>
/// Sets a window's icon from a <see cref="BitmapFrame"/> of the multi-size .ico, in code-behind. This
/// is what makes WPF populate BOTH the small (title-bar) and big (taskbar) icons — the XAML Icon
/// converter creates a single-frame image, so the taskbar button's big icon comes up blank. (This is
/// the same fix used in Emutastic.)
/// </summary>
internal static class WindowIcon
{
    public static void Apply(Window window, string packUri = "pack://application:,,,/Assets/EmuDOS.ico")
    {
        try
        {
            window.Icon = BitmapFrame.Create(new Uri(packUri));
            Log($"set Icon via BitmapFrame for {window.GetType().Name}");
        }
        catch (Exception ex)
        {
            Log($"failed for {window.GetType().Name}: {ex.Message}");
        }
    }

    private static void Log(string message)
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EmuDOS", "Logs");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "iconfix.log"), $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
        }
        catch { }
    }
}
