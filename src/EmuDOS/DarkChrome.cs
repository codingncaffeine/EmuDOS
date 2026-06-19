using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace EmuDOS;

/// <summary>Switches a window's OS title bar to dark mode so it matches the black/grey theme.</summary>
internal static partial class DarkChrome
{
    private const int DwmwaUseImmersiveDarkMode = 20;

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(nint hwnd, int attr, ref int value, int size);

    public static void Apply(Window window)
    {
        window.SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            int enabled = 1;
            DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref enabled, sizeof(int));
        };
    }
}
