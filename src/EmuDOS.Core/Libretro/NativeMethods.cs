using System.Runtime.InteropServices;

namespace EmuDOS.Core.Libretro;

/// <summary>
/// Win32 module loading. Uses <c>LoadLibraryEx</c> with <c>LOAD_WITH_ALTERED_SEARCH_PATH</c>
/// so a core's own directory is searched first for its dependent DLLs — letting a core ship
/// sibling runtime DLLs without polluting the app directory.
/// </summary>
internal static partial class NativeMethods
{
    public const uint LOAD_WITH_ALTERED_SEARCH_PATH = 0x00000008;

    // LibraryImport (unlike DllImport) does not auto-append the W/A suffix, so name the
    // Unicode export explicitly.
    [LibraryImport("kernel32.dll", EntryPoint = "LoadLibraryExW", SetLastError = true,
        StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint LoadLibraryEx(string lpFileName, nint hFile, uint dwFlags);

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    public static partial nint GetProcAddress(nint hModule, string lpProcName);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool FreeLibrary(nint hModule);
}
