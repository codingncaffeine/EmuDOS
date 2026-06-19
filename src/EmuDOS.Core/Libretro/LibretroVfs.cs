using System.IO;
using System.Runtime.InteropServices;

namespace EmuDOS.Core.Libretro;

/// <summary>
/// The libretro VFS (virtual file system) interface, version 3, backed by <see cref="System.IO"/>.
/// dosbox_pure calls this to enumerate the system directory (how it discovers installed-OS hard-disk
/// images for "[Run Installed Operating System]") and for some file I/O. Without it the core can still
/// create an OS image via plain fopen, but can never rediscover it — so we implement the full interface.
/// </summary>
internal sealed class LibretroVfs : IDisposable
{
    // Function-pointer types, in the exact order of struct retro_vfs_interface (v1 → v2 → v3).
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nint GetPathFn(nint stream);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nint OpenFn(nint path, uint mode, uint hints);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int CloseFn(nint stream);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate long SizeFn(nint stream);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate long TellFn(nint stream);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate long SeekFn(nint stream, long offset, int whence);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate long ReadFn(nint stream, nint s, ulong len);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate long WriteFn(nint stream, nint s, ulong len);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int FlushFn(nint stream);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int RemoveFn(nint path);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int RenameFn(nint oldPath, nint newPath);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate long TruncateFn(nint stream, long length);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int StatFn(nint path, nint size);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int MkdirFn(nint dir);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nint OpendirFn(nint dir, [MarshalAs(UnmanagedType.U1)] bool includeHidden);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.U1)] private delegate bool ReaddirFn(nint dir);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nint DirentGetNameFn(nint dir);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.U1)] private delegate bool DirentIsDirFn(nint dir);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int ClosedirFn(nint dir);

    // Keep-alive references (the core holds the thunks; let the GC reclaim none of them).
    private readonly GetPathFn _getPath;
    private readonly OpenFn _open;
    private readonly CloseFn _close;
    private readonly SizeFn _size;
    private readonly TellFn _tell;
    private readonly SeekFn _seek;
    private readonly ReadFn _read;
    private readonly WriteFn _write;
    private readonly FlushFn _flush;
    private readonly RemoveFn _remove;
    private readonly RenameFn _rename;
    private readonly TruncateFn _truncate;
    private readonly StatFn _stat;
    private readonly MkdirFn _mkdir;
    private readonly OpendirFn _opendir;
    private readonly ReaddirFn _readdir;
    private readonly DirentGetNameFn _direntGetName;
    private readonly DirentIsDirFn _direntIsDir;
    private readonly ClosedirFn _closedir;

    private readonly nint _interface;

    /// <summary>Pointer to the populated struct retro_vfs_interface (for GET_VFS_INTERFACE).</summary>
    public nint Interface => _interface;

    public LibretroVfs()
    {
        _getPath = GetPath; _open = Open; _close = Close; _size = Size; _tell = Tell; _seek = Seek;
        _read = Read; _write = Write; _flush = Flush; _remove = Remove; _rename = Rename; _truncate = Truncate;
        _stat = Stat; _mkdir = Mkdir; _opendir = Opendir; _readdir = Readdir; _direntGetName = DirentGetName;
        _direntIsDir = DirentIsDir; _closedir = Closedir;

        nint[] fns =
        [
            Marshal.GetFunctionPointerForDelegate(_getPath),
            Marshal.GetFunctionPointerForDelegate(_open),
            Marshal.GetFunctionPointerForDelegate(_close),
            Marshal.GetFunctionPointerForDelegate(_size),
            Marshal.GetFunctionPointerForDelegate(_tell),
            Marshal.GetFunctionPointerForDelegate(_seek),
            Marshal.GetFunctionPointerForDelegate(_read),
            Marshal.GetFunctionPointerForDelegate(_write),
            Marshal.GetFunctionPointerForDelegate(_flush),
            Marshal.GetFunctionPointerForDelegate(_remove),
            Marshal.GetFunctionPointerForDelegate(_rename),
            Marshal.GetFunctionPointerForDelegate(_truncate),
            Marshal.GetFunctionPointerForDelegate(_stat),
            Marshal.GetFunctionPointerForDelegate(_mkdir),
            Marshal.GetFunctionPointerForDelegate(_opendir),
            Marshal.GetFunctionPointerForDelegate(_readdir),
            Marshal.GetFunctionPointerForDelegate(_direntGetName),
            Marshal.GetFunctionPointerForDelegate(_direntIsDir),
            Marshal.GetFunctionPointerForDelegate(_closedir),
        ];
        _interface = Marshal.AllocHGlobal(fns.Length * nint.Size);
        for (int i = 0; i < fns.Length; i++)
            Marshal.WriteIntPtr(_interface, i * nint.Size, fns[i]);
    }

    public void Dispose()
    {
        if (_interface != 0)
            Marshal.FreeHGlobal(_interface);
    }

    private sealed class FileHandle { public required FileStream Stream; public nint PathPtr; }
    private sealed class DirHandle { public required List<string> Entries; public int Index = -1; public nint NamePtr; }

    private static string? Str(nint p) => p == 0 ? null : Marshal.PtrToStringAnsi(p);
    private static FileHandle? AsFile(nint s) => s == 0 ? null : GCHandle.FromIntPtr(s).Target as FileHandle;
    private static DirHandle? AsDir(nint d) => d == 0 ? null : GCHandle.FromIntPtr(d).Target as DirHandle;

    // --- file operations ---

    private nint Open(nint pathPtr, uint mode, uint hints)
    {
        var path = Str(pathPtr);
        if (path is null) return 0;
        try
        {
            // mode: bit0=READ, bit1=WRITE, bit2=UPDATE_EXISTING (open without truncating)
            var access = (mode & 3) switch { 1 => FileAccess.Read, 2 => FileAccess.Write, _ => FileAccess.ReadWrite };
            bool keep = (mode & 4) != 0;
            var fileMode = access == FileAccess.Read ? FileMode.Open : (keep ? FileMode.OpenOrCreate : FileMode.Create);
            var stream = new FileStream(path, fileMode, access, FileShare.ReadWrite);
            var handle = new FileHandle { Stream = stream, PathPtr = Marshal.StringToHGlobalAnsi(path) };
            return GCHandle.ToIntPtr(GCHandle.Alloc(handle));
        }
        catch { return 0; }
    }

    private nint GetPath(nint stream) => AsFile(stream)?.PathPtr ?? 0;
    private long Size(nint stream) { try { return AsFile(stream)?.Stream.Length ?? -1; } catch { return -1; } }
    private long Tell(nint stream) { try { return AsFile(stream)?.Stream.Position ?? -1; } catch { return -1; } }

    private long Seek(nint stream, long offset, int whence)
    {
        try
        {
            var f = AsFile(stream);
            if (f is null) return -1;
            var origin = whence switch { 1 => SeekOrigin.Current, 2 => SeekOrigin.End, _ => SeekOrigin.Begin };
            return f.Stream.Seek(offset, origin);
        }
        catch { return -1; }
    }

    private long Read(nint stream, nint s, ulong len)
    {
        try
        {
            var f = AsFile(stream);
            if (f is null) return -1;
            int n = (int)Math.Min(len, int.MaxValue);
            var buffer = new byte[n];
            int read = f.Stream.Read(buffer, 0, n);
            if (read > 0) Marshal.Copy(buffer, 0, s, read);
            return read;
        }
        catch { return -1; }
    }

    private long Write(nint stream, nint s, ulong len)
    {
        try
        {
            var f = AsFile(stream);
            if (f is null) return -1;
            int n = (int)Math.Min(len, int.MaxValue);
            var buffer = new byte[n];
            Marshal.Copy(s, buffer, 0, n);
            f.Stream.Write(buffer, 0, n);
            return n;
        }
        catch { return -1; }
    }

    private int Flush(nint stream) { try { AsFile(stream)?.Stream.Flush(); return 0; } catch { return -1; } }
    private long Truncate(nint stream, long length) { try { AsFile(stream)?.Stream.SetLength(length); return 0; } catch { return -1; } }

    private int Close(nint stream)
    {
        if (stream == 0) return -1;
        var gch = GCHandle.FromIntPtr(stream);
        try
        {
            if (gch.Target is FileHandle f)
            {
                f.Stream.Dispose();
                if (f.PathPtr != 0) Marshal.FreeHGlobal(f.PathPtr);
            }
        }
        catch { /* already closed / disposed */ }
        finally { gch.Free(); }
        return 0;
    }

    private int Remove(nint pathPtr) { try { var p = Str(pathPtr); if (p is null) return -1; File.Delete(p); return 0; } catch { return -1; } }

    private int Rename(nint oldPtr, nint newPtr)
    {
        try { var o = Str(oldPtr); var n = Str(newPtr); if (o is null || n is null) return -1; File.Move(o, n, overwrite: true); return 0; }
        catch { return -1; }
    }

    private int Stat(nint pathPtr, nint sizePtr)
    {
        var path = Str(pathPtr);
        if (path is null) return 0;
        try
        {
            if (Directory.Exists(path)) return 1 | 2; // VALID | IS_DIRECTORY
            var info = new FileInfo(path);
            if (!info.Exists) return 0;
            if (sizePtr != 0) Marshal.WriteInt32(sizePtr, (int)Math.Min(info.Length, int.MaxValue));
            return 1; // VALID
        }
        catch { return 0; }
    }

    private int Mkdir(nint dirPtr)
    {
        var dir = Str(dirPtr);
        if (dir is null) return -1;
        try { if (Directory.Exists(dir)) return -2; Directory.CreateDirectory(dir); return 0; }
        catch { return -1; }
    }

    // --- directory operations (the system-directory scan that finds installed OS images) ---

    private nint Opendir(nint dirPtr, bool includeHidden)
    {
        var dir = Str(dirPtr);
        if (dir is null || !Directory.Exists(dir)) return 0;
        try { return GCHandle.ToIntPtr(GCHandle.Alloc(new DirHandle { Entries = Directory.EnumerateFileSystemEntries(dir).ToList() })); }
        catch { return 0; }
    }

    private bool Readdir(nint dir)
    {
        var h = AsDir(dir);
        if (h is null) return false;
        return ++h.Index < h.Entries.Count;
    }

    private nint DirentGetName(nint dir)
    {
        var h = AsDir(dir);
        if (h is null || h.Index < 0 || h.Index >= h.Entries.Count) return 0;
        if (h.NamePtr != 0) Marshal.FreeHGlobal(h.NamePtr);
        h.NamePtr = Marshal.StringToHGlobalAnsi(Path.GetFileName(h.Entries[h.Index]));
        return h.NamePtr;
    }

    private bool DirentIsDir(nint dir)
    {
        var h = AsDir(dir);
        if (h is null || h.Index < 0 || h.Index >= h.Entries.Count) return false;
        try { return Directory.Exists(h.Entries[h.Index]); } catch { return false; }
    }

    private int Closedir(nint dir)
    {
        if (dir == 0) return -1;
        var gch = GCHandle.FromIntPtr(dir);
        try { if (gch.Target is DirHandle h && h.NamePtr != 0) Marshal.FreeHGlobal(h.NamePtr); }
        catch { }
        finally { gch.Free(); }
        return 0;
    }
}
