using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace EmuDOS;

/// <summary>
/// Sets a window's title-bar AND taskbar icons. The title bar reads the per-window icon (Window.Icon /
/// WM_SETICON); the taskbar button reads the window CLASS icon. A Windows 11 update (KB5051989) made
/// Explorer snapshot the class icon blank during early window creation — before WPF's per-window icon
/// is applied — so the taskbar comes up empty even though the title bar is correct. The fix is to write
/// the class icon directly (GCLP_HICON/GCLP_HICONSM) once the HWND exists. See dotnet/wpf #11308.
/// </summary>
internal static partial class WindowIcon
{
    private const int GCLP_HICON = -14;
    private const int GCLP_HICONSM = -34;
    private const uint IMAGE_ICON = 1;
    private const uint LR_LOADFROMFILE = 0x0010;
    private const uint LR_DEFAULTSIZE = 0x0040;

    [LibraryImport("user32.dll", EntryPoint = "LoadImageW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial nint LoadImage(nint hinst, string name, uint type, int cx, int cy, uint load);

    [LibraryImport("user32.dll", EntryPoint = "SetClassLongPtrW", SetLastError = true)]
    private static partial nint SetClassLongPtr(nint hwnd, int index, nint value);

    public static void Apply(Window window, string packUri = "pack://application:,,,/Assets/EmuDOS.ico")
    {
        // Title-bar (per-window) icon — this part already worked on its own.
        try { window.Icon = BitmapFrame.Create(new Uri(packUri)); } catch { }

        window.SourceInitialized += (_, _) =>
        {
            try
            {
                var res = Application.GetResourceStream(new Uri(packUri));
                if (res is null) { Log("resource null"); return; }

                // LoadImage needs a file, so spill the embedded .ico to disk once, then get HICONs.
                var path = Path.Combine(Path.GetTempPath(), "emudos_window.ico");
                using (var fs = File.Create(path))
                    res.Stream.CopyTo(fs);

                var hwnd = new WindowInteropHelper(window).Handle;
                var big = LoadImage(0, path, IMAGE_ICON, 0, 0, LR_LOADFROMFILE | LR_DEFAULTSIZE);
                var small = LoadImage(0, path, IMAGE_ICON, 16, 16, LR_LOADFROMFILE);

                // Write the CLASS icon — what the taskbar button reads (the bit WM_SETICON misses).
                if (big != 0) SetClassLongPtr(hwnd, GCLP_HICON, big);
                if (small != 0) SetClassLongPtr(hwnd, GCLP_HICONSM, small);
                Log($"{window.GetType().Name}: SetClassLongPtr big=0x{big:X} small=0x{small:X}");
            }
            catch (Exception ex) { Log($"failed: {ex.Message}"); }
        };
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
