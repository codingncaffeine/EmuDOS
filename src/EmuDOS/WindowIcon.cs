using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace EmuDOS;

/// <summary>
/// Forces a window's taskbar (big) and title-bar (small) icons via WM_SETICON. WPF's Window.Icon
/// reliably sets the small icon (so the title bar shows it) but the big icon — which the taskbar
/// button uses — can come up empty for a multi-size .ico. Setting both explicitly from the embedded
/// icon fixes the blank taskbar button.
/// </summary>
internal static partial class WindowIcon
{
    private const int WM_SETICON = 0x0080;
    private const nint ICON_SMALL = 0, ICON_BIG = 1;
    private const uint IMAGE_ICON = 1;
    private const uint LR_LOADFROMFILE = 0x0010;
    private const uint LR_DEFAULTSIZE = 0x0040;

    [LibraryImport("user32.dll", EntryPoint = "LoadImageW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint LoadImage(nint hinst, string name, uint type, int cx, int cy, uint load);

    [LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
    private static partial nint SendMessage(nint hwnd, int msg, nint wParam, nint lParam);

    public static void Apply(Window window, string packUri = "pack://application:,,,/Assets/EmuDOS.ico")
    {
        window.SourceInitialized += (_, _) =>
        {
            try
            {
                var res = Application.GetResourceStream(new System.Uri(packUri));
                if (res is null)
                    return;

                // LoadImage needs a file, so spill the embedded .ico to disk once.
                var path = Path.Combine(Path.GetTempPath(), "emudos_window.ico");
                using (var fs = File.Create(path))
                    res.Stream.CopyTo(fs);

                var hwnd = new WindowInteropHelper(window).Handle;
                var big = LoadImage(0, path, IMAGE_ICON, 0, 0, LR_LOADFROMFILE | LR_DEFAULTSIZE);
                var small = LoadImage(0, path, IMAGE_ICON, 16, 16, LR_LOADFROMFILE);
                if (big != 0) SendMessage(hwnd, WM_SETICON, ICON_BIG, big);
                if (small != 0) SendMessage(hwnd, WM_SETICON, ICON_SMALL, small);
            }
            catch { /* the icon is cosmetic — never let it break startup */ }
        };
    }
}
