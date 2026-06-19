using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using EmuDOS.Core.Engine;
using EmuDOS.Core.Engine.DosBoxPure;
using EmuDOS.Core.Input;
using EmuDOS.Core.Model;
using EmuDOS.Input;
using CorePixelFormat = EmuDOS.Core.Engine.PixelFormat;

namespace EmuDOS;

/// <summary>
/// Hosts a running game: it is the engine's <see cref="IEngineHost"/> — turning core video
/// frames into a <see cref="WriteableBitmap"/> and feeding keyboard/mouse back as input.
/// Audio is wired in a follow-up; the session paces by wall clock, so the game runs at the
/// right speed regardless.
/// </summary>
public partial class EmulatorWindow : Window, IEngineHost, IInputSource
{
    private readonly IDosSession _session;
    private readonly object _frameLock = new();
    private byte[] _frameBuffer = [];
    private int _frameWidth;
    private int _frameHeight;
    private WriteableBitmap? _bitmap;
    private int _renderQueued;

    private readonly object _inputLock = new();
    private readonly HashSet<DosKey> _keysDown = [];
    private int _mouseDx;
    private int _mouseDy;
    private bool _mouseLeft;
    private bool _mouseRight;
    private bool _mouseMiddle;
    private Point? _lastMouse;

    public EmulatorWindow(IDosEngine engine, GameInstance instance)
    {
        InitializeComponent();
        Title = $"EmuDOS — {instance.Profile.Title}";
        _session = engine.CreateSession(instance, this);
        Loaded += (_, _) => { _session.Start(); Focus(); };
    }

    public IInputSource Input => this;

    // ── IEngineHost ───────────────────────────────────────────────────────────

    public void SubmitVideoFrame(in VideoFrame frame)
    {
        int w = frame.Width, h = frame.Height;
        if (w <= 0 || h <= 0)
            return;

        lock (_frameLock)
        {
            int needed = w * h * 4;
            if (_frameBuffer.Length < needed)
                _frameBuffer = new byte[needed];

            if (frame.Format == CorePixelFormat.Xrgb8888)
                CopyXrgb8888(frame, w, h);
            else
                CopyRgb565(frame, w, h);

            _frameWidth = w;
            _frameHeight = h;
        }

        if (Interlocked.CompareExchange(ref _renderQueued, 1, 0) == 0)
            Dispatcher.BeginInvoke(RenderFrame);
    }

    public void SubmitAudioFrames(ReadOnlySpan<short> interleavedStereo)
    {
        // Audio output is added in a follow-up; ignored for now.
    }

    private void CopyXrgb8888(in VideoFrame frame, int w, int h)
    {
        // XRGB8888 little-endian is byte order B,G,R,X — identical to WPF Bgr32.
        for (int y = 0; y < h; y++)
            Marshal.Copy(frame.Data + (y * frame.Pitch), _frameBuffer, y * w * 4, w * 4);
    }

    private unsafe void CopyRgb565(in VideoFrame frame, int w, int h)
    {
        for (int y = 0; y < h; y++)
        {
            var row = (ushort*)(frame.Data + (y * frame.Pitch));
            int dst = y * w * 4;
            for (int x = 0; x < w; x++)
            {
                ushort p = row[x];
                int r = (p >> 11) & 0x1F, g = (p >> 5) & 0x3F, b = p & 0x1F;
                _frameBuffer[dst++] = (byte)((b << 3) | (b >> 2));
                _frameBuffer[dst++] = (byte)((g << 2) | (g >> 4));
                _frameBuffer[dst++] = (byte)((r << 3) | (r >> 2));
                _frameBuffer[dst++] = 0;
            }
        }
    }

    private void RenderFrame()
    {
        Interlocked.Exchange(ref _renderQueued, 0);
        lock (_frameLock)
        {
            if (_frameWidth <= 0)
                return;

            if (_bitmap is null || _bitmap.PixelWidth != _frameWidth || _bitmap.PixelHeight != _frameHeight)
            {
                _bitmap = new WriteableBitmap(_frameWidth, _frameHeight, 96, 96, PixelFormats.Bgr32, null);
                Screen.Source = _bitmap;
            }

            _bitmap.WritePixels(
                new Int32Rect(0, 0, _frameWidth, _frameHeight), _frameBuffer, _frameWidth * 4, 0);
        }
    }

    // ── IInputSource ──────────────────────────────────────────────────────────

    public bool IsKeyDown(DosKey key)
    {
        lock (_inputLock)
            return _keysDown.Contains(key);
    }

    public MouseDelta PollMouse()
    {
        lock (_inputLock)
        {
            var delta = new MouseDelta(_mouseDx, _mouseDy, _mouseLeft, _mouseRight, _mouseMiddle);
            _mouseDx = 0;
            _mouseDy = 0;
            return delta;
        }
    }

    public bool IsButtonDown(int port, PadButton button) => false;

    // ── Input events ──────────────────────────────────────────────────────────

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        var key = KeyMap.ToDosKey(e.Key == Key.System ? e.SystemKey : e.Key);
        if (key != DosKey.None)
        {
            lock (_inputLock)
                _keysDown.Add(key);
            e.Handled = true;
        }
    }

    private void OnKeyUp(object sender, KeyEventArgs e)
    {
        var key = KeyMap.ToDosKey(e.Key == Key.System ? e.SystemKey : e.Key);
        if (key != DosKey.None)
        {
            lock (_inputLock)
                _keysDown.Remove(key);
            e.Handled = true;
        }
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        var pos = e.GetPosition(this);
        if (_lastMouse is { } last)
        {
            lock (_inputLock)
            {
                _mouseDx += (int)(pos.X - last.X);
                _mouseDy += (int)(pos.Y - last.Y);
            }
        }

        _lastMouse = pos;
    }

    private void OnMouseButton(object sender, MouseButtonEventArgs e)
    {
        bool down = e.ButtonState == MouseButtonState.Pressed;
        lock (_inputLock)
        {
            switch (e.ChangedButton)
            {
                case MouseButton.Left: _mouseLeft = down; break;
                case MouseButton.Right: _mouseRight = down; break;
                case MouseButton.Middle: _mouseMiddle = down; break;
            }
        }

        Focus();
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _session.Stop();
        _session.Dispose();
    }
}
