using System;
using System.Runtime.InteropServices;

namespace EmuDOS.Core.Libretro;

/// <summary>
/// A libretro OpenGL hardware-render channel for a single core (dosbox_pure): creates an offscreen
/// WGL context + FBO on the calling thread, satisfies the core's <c>SET_HW_RENDER</c> negotiation,
/// and reads the rendered FBO back to a CPU buffer (GL_BGRA == libretro XRGB8888 byte layout) so the
/// existing software present path can display it. Adapted from Emutastic's GL pipeline, simplified to
/// one core's policy (offscreen window, own FBO, readback).
///
/// THREAD AFFINITY: every method must run on the one thread that owns the GL context (the dosbox
/// thread). Create during load, read during run, Destroy on teardown — all on that thread.
/// </summary>
internal sealed class GlHwRender : IDisposable
{
    private readonly Action<int, string>? _log;

    // ── HW-render callback ABI (x64 explicit layout; 4 bytes pad after context_type) ──────────
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void ContextResetT();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate ulong GetCurrentFramebufferT();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr GetProcAddressT([MarshalAs(UnmanagedType.LPStr)] string sym);

    private ContextResetT? _coreContextReset;       // SET BY CORE — we call it after LoadGame
    private ContextResetT? _coreContextDestroy;     // SET BY CORE — we call it on teardown
    private GetCurrentFramebufferT? _getFramebuffer; // SET BY US — core calls it for the FBO id
    private GetProcAddressT? _getProcAddress;        // SET BY US — core calls it to resolve GL symbols
    private GCHandle _fbHandle, _paHandle;

    private IntPtr _hwnd, _hdc, _hglrc;
    private uint _fboId, _fboTex, _fboDepth;
    private int _fboWidth = 640, _fboHeight = 480;
    private bool _coreProfile;

    private IntPtr _readback;       // persistent BGRA buffer (top-down, alpha forced 0xFF)
    private int _readbackBytes;
    private bool _fbLogged, _frameLogged;
    private readonly System.Diagnostics.Stopwatch _rbSw = new();
    private double _rbSumMs, _rbMaxMs;
    private int _rbFrames;

    public bool Active { get; private set; }
    public IntPtr FramePtr => _readback;
    public int FrameWidth { get; private set; }
    public int FrameHeight { get; private set; }

    public GlHwRender(Action<int, string>? log) => _log = log;
    private void Log(string m) => _log?.Invoke(1, "[gl] " + m);

    /// <summary>Handle <c>RETRO_ENVIRONMENT_SET_HW_RENDER</c>: accept an OpenGL context, build it +
    /// an FBO, store the core's reset/destroy callbacks and write our framebuffer/proc-address
    /// pointers back into the struct. Returns false for non-GL requests (caller then refuses HW).</summary>
    public bool Negotiate(IntPtr data)
    {
        if (data == IntPtr.Zero)
            return false;

        uint contextType = (uint)Marshal.ReadInt32(data, 0);
        if (contextType != LibretroConstants.HwContextOpenGL && contextType != LibretroConstants.HwContextOpenGLCore)
        {
            Log($"refusing context_type={contextType} (not OpenGL)");
            return false;
        }
        _coreProfile = contextType == LibretroConstants.HwContextOpenGLCore;

        if (!CreateContext())
            return false;
        LoadGlFunctions();
        CreateFbo(_fboWidth, _fboHeight);

        IntPtr reset = Marshal.ReadIntPtr(data, 8);
        IntPtr destroy = Marshal.ReadIntPtr(data, 48);
        _coreContextReset = reset != IntPtr.Zero ? Marshal.GetDelegateForFunctionPointer<ContextResetT>(reset) : null;
        _coreContextDestroy = destroy != IntPtr.Zero ? Marshal.GetDelegateForFunctionPointer<ContextResetT>(destroy) : null;

        _getFramebuffer = () =>
        {
            if (!_fbLogged) { _fbLogged = true; Log($"core called get_current_framebuffer -> {_fboId}"); }
            return _fboId;
        };
        _getProcAddress = ResolveProc;
        _fbHandle = GCHandle.Alloc(_getFramebuffer);
        _paHandle = GCHandle.Alloc(_getProcAddress);
        Marshal.WriteIntPtr(data, 16, Marshal.GetFunctionPointerForDelegate(_getFramebuffer));
        Marshal.WriteIntPtr(data, 24, Marshal.GetFunctionPointerForDelegate(_getProcAddress));

        Active = true;
        Log($"SET_HW_RENDER accepted (coreProfile={_coreProfile}); context_reset deferred to post-LoadGame");
        return true;
    }

    /// <summary>Resize the FBO to fit the core's max geometry, then call the core's context_reset with
    /// our context current. Per the libretro spec this happens after retro_load_game returns.</summary>
    public void PrepareAndReset(int maxWidth, int maxHeight)
    {
        if (!Active)
            return;
        Gl.wglMakeCurrent(_hdc, _hglrc);
        if (maxWidth > _fboWidth || maxHeight > _fboHeight)
            CreateFbo(Math.Max(maxWidth, _fboWidth), Math.Max(maxHeight, _fboHeight));
        Log($"calling context_reset (fbo {_fboWidth}x{_fboHeight})");
        try { _coreContextReset?.Invoke(); } catch (Exception ex) { Log("context_reset threw: " + ex.Message); }
        Log("context_reset done");
    }

    /// <summary>Read the FBO (after retro_run) into the persistent CPU buffer, vertically flipped with
    /// alpha forced opaque, and report the size. Runs on the GL thread with the context current.</summary>
    public bool ReadFrame(int width, int height)
    {
        if (!Active || width <= 0 || height <= 0)
            return false;
        int bytes = width * height * 4;
        if (_readbackBytes != bytes)
        {
            if (_readback != IntPtr.Zero) Marshal.FreeHGlobal(_readback);
            _readback = Marshal.AllocHGlobal(bytes);
            _readbackBytes = bytes;
        }

        int stride = width * 4;
        _rbSw.Restart(); // time the readback to tell our pipeline cost apart from a core/game stutter
        IntPtr tmp = Marshal.AllocHGlobal(bytes);
        try
        {
            Gl.glBindFramebuffer(GlBindReadTarget, _fboId);
            Gl.glReadPixels(0, 0, width, height, Gl.GL_BGRA, Gl.GL_UNSIGNED_BYTE, tmp);
            Gl.glBindFramebuffer(GlBindReadTarget, 0);
            unsafe
            {
                byte* src = (byte*)tmp;       // bottom-up from GL
                byte* dst = (byte*)_readback; // top-down for WPF
                for (int y = 0; y < height; y++)
                {
                    Buffer.MemoryCopy(src + y * stride, dst + (height - 1 - y) * stride, stride, stride);
                    byte* row = dst + (height - 1 - y) * stride;
                    for (int x = 3; x < stride; x += 4) row[x] = 0xFF; // force opaque (cores leave A=0)
                }
            }
        }
        finally { Marshal.FreeHGlobal(tmp); }

        double ms = _rbSw.Elapsed.TotalMilliseconds;
        _rbSumMs += ms;
        if (ms > _rbMaxMs) _rbMaxMs = ms;
        if (++_rbFrames >= 300)
        {
            Log($"readback avg {_rbSumMs / _rbFrames:0.00} ms, max {_rbMaxMs:0.00} ms over {_rbFrames} frames ({width}x{height})");
            _rbSumMs = 0; _rbMaxMs = 0; _rbFrames = 0;
        }

        if (!_frameLogged)
        {
            _frameLogged = true;
            // Compare our FBO vs the default framebuffer (FBO 0): tells us whether the core actually
            // rendered into the FBO we handed it, or to the default backbuffer instead.
            Log($"first HW frame {width}x{height}: ourFbo(id={_fboId}) nonBlack={SampleNonBlack(_fboId, width, height)}, " +
                $"defaultFbo(0) nonBlack={SampleNonBlack(0, width, height)}");
        }

        FrameWidth = width;
        FrameHeight = height;
        return true;
    }

    private const uint GlBindReadTarget = 0x8CA8; // GL_READ_FRAMEBUFFER

    // Diagnostic: read a framebuffer and report whether any sampled pixel is non-black.
    private bool SampleNonBlack(uint fbo, int width, int height)
    {
        int bytes = width * height * 4;
        IntPtr buf = Marshal.AllocHGlobal(bytes);
        try
        {
            Gl.glBindFramebuffer(GlBindReadTarget, fbo);
            Gl.glReadPixels(0, 0, width, height, Gl.GL_BGRA, Gl.GL_UNSIGNED_BYTE, buf);
            Gl.glBindFramebuffer(GlBindReadTarget, 0);
            unsafe
            {
                byte* p = (byte*)buf;
                for (int i = 0; i + 2 < bytes; i += 4096)
                    if (p[i] != 0 || p[i + 1] != 0 || p[i + 2] != 0) return true;
            }
            return false;
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    // ── Context + FBO ─────────────────────────────────────────────────────────────────────────
    private bool CreateContext()
    {
        const uint WS_POPUP = 0x80000000;
        const uint CS_OWNDC = 0x0020;
        _wndProc = Gl.DefWindowProc; // keep alive
        var wc = new Gl.WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<Gl.WNDCLASSEX>(),
            style = CS_OWNDC,
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            hInstance = Gl.GetModuleHandle(null),
            lpszClassName = "EmuDosGlOffscreen",
        };
        Gl.RegisterClassEx(ref wc); // no-op if already registered

        _hwnd = Gl.CreateWindowEx(0, "EmuDosGlOffscreen", "GLOffscreen", WS_POPUP, 0, 0, 640, 480,
            IntPtr.Zero, IntPtr.Zero, Gl.GetModuleHandle(null), IntPtr.Zero);
        if (_hwnd == IntPtr.Zero) { Log("CreateWindowEx failed"); return false; }
        _hdc = Gl.GetDC(_hwnd);
        if (_hdc == IntPtr.Zero) { Log("GetDC failed"); return false; }

        var pfd = new Gl.PIXELFORMATDESCRIPTOR
        {
            nSize = (ushort)Marshal.SizeOf<Gl.PIXELFORMATDESCRIPTOR>(),
            nVersion = 1,
            dwFlags = Gl.PFD_DRAW_TO_WINDOW | Gl.PFD_SUPPORT_OPENGL, // no double-buffer: we read an FBO
            iPixelType = Gl.PFD_TYPE_RGBA,
            cColorBits = 32,
            cDepthBits = 24,
            cStencilBits = 8,
        };
        int fmt = Gl.ChoosePixelFormat(_hdc, ref pfd);
        if (fmt == 0 || !Gl.SetPixelFormat(_hdc, fmt, ref pfd)) { Log("Choose/SetPixelFormat failed"); return false; }

        IntPtr dummy = Gl.wglCreateContext(_hdc);
        if (dummy == IntPtr.Zero || !Gl.wglMakeCurrent(_hdc, dummy)) { Log("dummy context failed"); return false; }

        var createAttribs = GetProc<Gl.WglCreateContextAttribsArb>("wglCreateContextAttribsARB");
        if (createAttribs is null)
        {
            _hglrc = dummy;
        }
        else
        {
            int profile = _coreProfile ? Gl.WGL_CONTEXT_CORE_PROFILE_BIT_ARB : Gl.WGL_CONTEXT_COMPATIBILITY_PROFILE_BIT_ARB;
            int[] attribs = { Gl.WGL_CONTEXT_MAJOR_VERSION_ARB, 3, Gl.WGL_CONTEXT_MINOR_VERSION_ARB, 3,
                              Gl.WGL_CONTEXT_PROFILE_MASK_ARB, profile, 0 };
            _hglrc = createAttribs(_hdc, IntPtr.Zero, attribs);
            if (_hglrc == IntPtr.Zero) // fall back to the other profile
            {
                attribs[5] = _coreProfile ? Gl.WGL_CONTEXT_COMPATIBILITY_PROFILE_BIT_ARB : Gl.WGL_CONTEXT_CORE_PROFILE_BIT_ARB;
                _hglrc = createAttribs(_hdc, IntPtr.Zero, attribs);
            }
            if (_hglrc == IntPtr.Zero) { _hglrc = dummy; }
            else { Gl.wglMakeCurrent(IntPtr.Zero, IntPtr.Zero); Gl.wglDeleteContext(dummy); }
        }
        if (!Gl.wglMakeCurrent(_hdc, _hglrc)) { Log("final wglMakeCurrent failed"); return false; }
        Log($"context ready hglrc=0x{_hglrc:X}");
        return true;
    }

    private void CreateFbo(int width, int height)
    {
        DestroyFbo();
        _fboWidth = width; _fboHeight = height;
        uint[] ids = new uint[1];

        Gl.glGenTextures(1, ids); _fboTex = ids[0];
        Gl.glBindTexture(Gl.GL_TEXTURE_2D, _fboTex);
        Gl.glTexImage2D(Gl.GL_TEXTURE_2D, 0, Gl.GL_RGBA8, width, height, 0, Gl.GL_RGBA, Gl.GL_UNSIGNED_BYTE, IntPtr.Zero);
        Gl.glTexParameteri(Gl.GL_TEXTURE_2D, Gl.GL_TEXTURE_MIN_FILTER, (int)Gl.GL_LINEAR);
        Gl.glTexParameteri(Gl.GL_TEXTURE_2D, Gl.GL_TEXTURE_MAG_FILTER, (int)Gl.GL_LINEAR);
        Gl.glBindTexture(Gl.GL_TEXTURE_2D, 0);

        Gl.glGenRenderbuffers(1, ids); _fboDepth = ids[0];
        Gl.glBindRenderbuffer(Gl.GL_RENDERBUFFER, _fboDepth);
        Gl.glRenderbufferStorage(Gl.GL_RENDERBUFFER, Gl.GL_DEPTH_COMPONENT24, width, height);
        Gl.glBindRenderbuffer(Gl.GL_RENDERBUFFER, 0);

        Gl.glGenFramebuffers(1, ids); _fboId = ids[0];
        Gl.glBindFramebuffer(Gl.GL_FRAMEBUFFER, _fboId);
        Gl.glFramebufferTexture2D(Gl.GL_FRAMEBUFFER, Gl.GL_COLOR_ATTACHMENT0, Gl.GL_TEXTURE_2D, _fboTex, 0);
        Gl.glFramebufferRenderbuffer(Gl.GL_FRAMEBUFFER, Gl.GL_DEPTH_ATTACHMENT, Gl.GL_RENDERBUFFER, _fboDepth);
        uint status = Gl.glCheckFramebufferStatus(Gl.GL_FRAMEBUFFER);
        Log(status == Gl.GL_FRAMEBUFFER_COMPLETE ? $"FBO ok {width}x{height} id={_fboId}" : $"FBO incomplete 0x{status:X}");
        Gl.glBindFramebuffer(Gl.GL_FRAMEBUFFER, 0);
    }

    private IntPtr ResolveProc(string sym)
    {
        IntPtr p = Gl.wglGetProcAddress(sym);
        if (p == IntPtr.Zero || (long)p is >= 1 and <= 3)
        {
            IntPtr lib = Gl.GetModuleHandle("opengl32.dll");
            if (lib == IntPtr.Zero) lib = Gl.LoadLibrary("opengl32.dll");
            if (lib != IntPtr.Zero) p = Gl.GetProcAddress(lib, sym);
        }
        return p;
    }

    private T? GetProc<T>(string name) where T : class
    {
        IntPtr p = ResolveProc(name);
        return p == IntPtr.Zero ? null : Marshal.GetDelegateForFunctionPointer<T>(p);
    }

    private Gl.WndProc? _wndProc;
    private void LoadGlFunctions() => Gl.Load(ResolveProc);

    private void DestroyFbo()
    {
        uint[] ids = new uint[1];
        if (_fboId != 0) { ids[0] = _fboId; Gl.glDeleteFramebuffers(1, ids); _fboId = 0; }
        if (_fboTex != 0) { ids[0] = _fboTex; Gl.glDeleteTextures(1, ids); _fboTex = 0; }
        if (_fboDepth != 0) { ids[0] = _fboDepth; Gl.glDeleteRenderbuffers(1, ids); _fboDepth = 0; }
    }

    public void Dispose()
    {
        if (!Active && _hglrc == IntPtr.Zero)
            return;
        try
        {
            if (_hdc != IntPtr.Zero) Gl.wglMakeCurrent(_hdc, _hglrc);
            try { _coreContextDestroy?.Invoke(); } catch (Exception ex) { Log("context_destroy threw: " + ex.Message); }
            DestroyFbo();
            Gl.wglMakeCurrent(IntPtr.Zero, IntPtr.Zero);
            if (_hglrc != IntPtr.Zero) Gl.wglDeleteContext(_hglrc);
            if (_hwnd != IntPtr.Zero && _hdc != IntPtr.Zero) Gl.ReleaseDC(_hwnd, _hdc);
            if (_hwnd != IntPtr.Zero) Gl.DestroyWindow(_hwnd);
        }
        catch (Exception ex) { Log("teardown: " + ex.Message); }
        if (_readback != IntPtr.Zero) { Marshal.FreeHGlobal(_readback); _readback = IntPtr.Zero; }
        if (_fbHandle.IsAllocated) _fbHandle.Free();
        if (_paHandle.IsAllocated) _paHandle.Free();
        _hglrc = _hdc = _hwnd = IntPtr.Zero;
        Active = false;
    }
}
