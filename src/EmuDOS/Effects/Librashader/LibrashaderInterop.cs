using System;
using System.Runtime.InteropServices;

namespace EmuDOS.Effects.Librashader;

/// <summary>
/// P/Invoke bindings for the librashader C ABI (Direct3D 11 runtime). librashader is RetroArch's
/// shader runtime as a standalone library; we load <c>librashader.dll</c> dynamically (downloaded on
/// demand into the data folder, so it is NOT resolvable by a static [DllImport]) and bind only the
/// D3D11 + preset + error entry points we use. A returned <c>libra_error_t</c> of
/// <see cref="IntPtr.Zero"/> means success.
/// </summary>
internal static class LibrashaderInterop
{
    /// <summary>Mirror of <c>libra_viewport_t</c> { float x, y; uint32 width, height; }.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct LibraViewport
    {
        public float X;
        public float Y;
        public uint Width;
        public uint Height;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate IntPtr PresetCreateDelegate(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string filename, out IntPtr presetOut);

    // 'preset' is consumed (invalidated) by this call — pass by ref; do not free afterwards.
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate IntPtr D3D11ChainCreateDelegate(
        ref IntPtr preset, IntPtr device, IntPtr options, out IntPtr chainOut);

    // 'chain' is a POINTER to the opaque handle (same indirection as create/free) — pass by ref.
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate IntPtr D3D11ChainFrameDelegate(
        ref IntPtr chain, IntPtr deviceContext, UIntPtr frameCount,
        IntPtr inputSrv, IntPtr outputRtv, ref LibraViewport viewport,
        IntPtr mvp, IntPtr options);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate IntPtr D3D11ChainFreeDelegate(ref IntPtr chain);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate int ErrorFreeDelegate(ref IntPtr error);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate int ErrorErrnoDelegate(IntPtr error);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate ulong AbiVersionDelegate();

    public static PresetCreateDelegate PresetCreate = null!;
    public static D3D11ChainCreateDelegate D3D11ChainCreate = null!;
    public static D3D11ChainFrameDelegate D3D11ChainFrame = null!;
    public static D3D11ChainFreeDelegate D3D11ChainFree = null!;
    public static ErrorFreeDelegate ErrorFree = null!;
    public static ErrorErrnoDelegate ErrorErrno = null!;
    public static AbiVersionDelegate AbiVersion = null!;

    private static IntPtr _handle = IntPtr.Zero;
    private static readonly object _gate = new();

    public static bool Loaded => _handle != IntPtr.Zero;

    /// <summary>Loads librashader.dll and binds the entry points. Returns false on any failure
    /// (caller falls back to no shader). Idempotent.</summary>
    public static bool Load(string dllPath)
    {
        lock (_gate)
        {
            if (Loaded) return true;
            try
            {
                if (string.IsNullOrWhiteSpace(dllPath) || !System.IO.File.Exists(dllPath)) return false;
                if (!NativeLibrary.TryLoad(dllPath, out _handle)) { _handle = IntPtr.Zero; return false; }

                PresetCreate = Bind<PresetCreateDelegate>("libra_preset_create");
                D3D11ChainCreate = Bind<D3D11ChainCreateDelegate>("libra_d3d11_filter_chain_create");
                D3D11ChainFrame = Bind<D3D11ChainFrameDelegate>("libra_d3d11_filter_chain_frame");
                D3D11ChainFree = Bind<D3D11ChainFreeDelegate>("libra_d3d11_filter_chain_free");
                ErrorFree = Bind<ErrorFreeDelegate>("libra_error_free");
                ErrorErrno = Bind<ErrorErrnoDelegate>("libra_error_errno");
                AbiVersion = Bind<AbiVersionDelegate>("libra_instance_abi_version");
                return true;
            }
            catch
            {
                Unload();
                return false;
            }
        }
    }

    private static T Bind<T>(string export) where T : Delegate
        => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(_handle, export));

    public static void Unload()
    {
        lock (_gate)
        {
            if (_handle != IntPtr.Zero)
            {
                try { NativeLibrary.Free(_handle); } catch { /* best effort */ }
                _handle = IntPtr.Zero;
            }
        }
    }

    /// <summary>If <paramref name="error"/> is non-null, reads its errno, frees it, and returns the
    /// code (0 = no error). Always nulls the handle.</summary>
    public static int ConsumeError(IntPtr error)
    {
        if (error == IntPtr.Zero) return 0;
        int code = -1;
        try { code = ErrorErrno(error); } catch { }
        try { ErrorFree(ref error); } catch { }
        return code;
    }
}
