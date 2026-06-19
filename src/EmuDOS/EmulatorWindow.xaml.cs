using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EmuDOS.Core.Engine;
using EmuDOS.Core.Engine.DosBoxPure;
using EmuDOS.Core.Infrastructure;
using EmuDOS.Core.Input;
using EmuDOS.Core.Model;
using EmuDOS.Input;
using NAudio.Wave;
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

    private WaveOutEvent? _audioOut;
    private BufferedWaveProvider? _audioBuffer;
    private byte[] _audioBytes = [];
    private int _audioBatches;

    private readonly byte[]? _lut; // brightness/gamma lookup; null = no adjustment (fast path)

    private Mt32LcdWindow? _lcdWindow;
    private DispatcherTimer? _lcdTimer;

    private readonly GameInstance _instance;
    private readonly AppLog _log;

    private readonly object _inputLock = new();
    private readonly HashSet<DosKey> _keysDown = [];
    private readonly System.Collections.Concurrent.ConcurrentQueue<KeyEvent> _keyEvents = new();
    // Accumulate fractional movement so slow, precise moves aren't truncated to zero; the integer
    // part is sent each poll and the remainder carried. Scaled to physical pixels (× DPI) and a
    // sensitivity factor so the in-game cursor keeps pace with the real one at any window size.
    private const double MouseSensitivity = 1.5;
    private double _mouseAccumX;
    private double _mouseAccumY;
    private double _mouseScale = MouseSensitivity;
    private double _sensitivity = MouseSensitivity;
    private double _dpiScale = 1.0;
    private bool _mouseLocked;
    private bool _mouseLeft;
    private bool _mouseRight;
    private bool _mouseMiddle;
    private DispatcherTimer? _hintTimer;
    private Point? _lastMouse;

    public EmulatorWindow(IDosEngine engine, GameInstance instance)
    {
        InitializeComponent();
        DarkChrome.Apply(this);
        _instance = instance;
        Title = $"EmuDOS — {instance.Profile.Title}";
        _log = new AppLog(((App)Application.Current).Services.Paths, "emulator.log");
        _log.Info($"Launch '{instance.Profile.Title}' exe={instance.Profile.Launch.Executable ?? "(autoexec)"}");
        _lut = BuildLut(instance.Profile.Display);

        // Restore the window size this game was last played at (saved on close).
        var state = ((App)Application.Current).Services.Store.ReadState(instance.GameboxPath);
        if (state.WindowWidth is int savedW and > 200 && state.WindowHeight is int savedH and > 150)
        {
            Width = savedW;
            Height = savedH;
        }

        _session = engine.CreateSession(instance, this);
        Loaded += OnLoadedGrabFocus;
        Activated += (_, _) => Keyboard.Focus(this);
    }

    private void OnLoadedGrabFocus(object sender, RoutedEventArgs e)
    {
        _session.Start();
        _dpiScale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        _mouseScale = _dpiScale * _sensitivity;
        Deactivated += (_, _) => { if (_mouseLocked) ToggleMouseLock(); };
        Dispatcher.BeginInvoke(() =>
        {
            Activate();
            Focus();
            Keyboard.Focus(this);
            _log.Info($"Loaded IsActive={IsActive} KbFocused={IsKeyboardFocused} FocusWithin={IsKeyboardFocusWithin}");
        }, DispatcherPriority.Input);

        // Poll the MT-32 LCD; the synth is created asynchronously on the engine thread, so show
        // the display the moment it appears and keep its text updated.
        _lcdTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _lcdTimer.Tick += UpdateLcd;
        _lcdTimer.Start();

        // A freshly-imported CD boots to DOS with the disc on D: — tell the user how to install.
        if (string.IsNullOrWhiteSpace(_instance.Profile.Launch.Executable)
            && _instance.Profile.Launch.PreCommands.Any(c => c.Contains("IMGMOUNT", StringComparison.OrdinalIgnoreCase)))
        {
            ShowHint("Disc mounted as D:  —  type  D:  then run the installer (SETUP or INSTALL)", 7);
        }
    }

    private void UpdateLcd(object? sender, EventArgs e)
    {
        var text = _session.Mt32Lcd;
        if (text is null)
            return; // MT-32 synth not active for this game

        if (_lcdWindow is null)
        {
            _lcdWindow = new Mt32LcdWindow { Owner = this };
            _lcdWindow.Show();
            _lcdWindow.Left = Left + Math.Max(0, (ActualWidth - _lcdWindow.ActualWidth) / 2);
            _lcdWindow.Top = Top + Math.Max(0, ActualHeight - _lcdWindow.ActualHeight - 28);
        }

        _lcdWindow.SetText(text);
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

            ApplyLut(w * h);

            _frameWidth = w;
            _frameHeight = h;
        }

        if (Interlocked.CompareExchange(ref _renderQueued, 1, 0) == 0)
            Dispatcher.BeginInvoke(RenderFrame);
    }

    public void SetAudioSampleRate(int sampleRate)
    {
        Dispatcher.Invoke(() =>
        {
            try
            {
                _audioBuffer = new BufferedWaveProvider(new WaveFormat(sampleRate, 16, 2))
                {
                    DiscardOnBufferOverflow = true,
                    BufferDuration = TimeSpan.FromMilliseconds(400),
                };
                _audioOut = new WaveOutEvent { DesiredLatency = 100 };
                _audioOut.Init(_audioBuffer);
                _audioOut.Play();
                _log.Info($"Audio init {sampleRate}Hz, state={_audioOut.PlaybackState}");
            }
            catch (Exception ex)
            {
                _log.Error($"Audio init failed: {ex.Message}");
            }
        });
    }

    public void SubmitAudioFrames(ReadOnlySpan<short> interleavedStereo)
    {
        var buffer = _audioBuffer;
        if (buffer is null || interleavedStereo.IsEmpty)
            return;

        var source = MemoryMarshal.AsBytes(interleavedStereo);
        if (_audioBytes.Length < source.Length)
            _audioBytes = new byte[source.Length];
        source.CopyTo(_audioBytes);
        buffer.AddSamples(_audioBytes, 0, source.Length);

        if (++_audioBatches % 300 == 1)
            _log.Info($"audio #{_audioBatches} buffered={buffer.BufferedBytes}B | input: {_session.InputDiagnostics}");
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

    private static byte[]? BuildLut(DisplaySpec display)
    {
        bool identity = Math.Abs(display.Brightness - 1.0) < 0.001 && Math.Abs(display.Gamma - 1.0) < 0.001;
        if (identity)
            return null;

        double invGamma = 1.0 / Math.Max(0.1, display.Gamma);
        var lut = new byte[256];
        for (int i = 0; i < 256; i++)
        {
            double v = Math.Pow(i / 255.0, invGamma) * display.Brightness;
            lut[i] = (byte)Math.Clamp(v * 255.0, 0.0, 255.0);
        }

        return lut;
    }

    private void ApplyLut(int pixelCount)
    {
        var lut = _lut;
        if (lut is null)
            return;

        var buf = _frameBuffer;
        int n = pixelCount * 4;
        for (int i = 0; i + 2 < n; i += 4)
        {
            buf[i] = lut[buf[i]];         // B
            buf[i + 1] = lut[buf[i + 1]]; // G
            buf[i + 2] = lut[buf[i + 2]]; // R
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

    public bool TryDequeueKey(out KeyEvent keyEvent) => _keyEvents.TryDequeue(out keyEvent);

    public MouseDelta PollMouse()
    {
        lock (_inputLock)
        {
            int dx = (int)_mouseAccumX;
            int dy = (int)_mouseAccumY;
            _mouseAccumX -= dx; // carry the sub-pixel remainder
            _mouseAccumY -= dy;
            return new MouseDelta(dx, dy, _mouseLeft, _mouseRight, _mouseMiddle);
        }
    }

    public bool IsButtonDown(int port, PadButton button) => false;

    // ── Input events ──────────────────────────────────────────────────────────

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        var key = KeyMap.ToDosKey(e.Key == Key.System ? e.SystemKey : e.Key);
        if (key == DosKey.None)
            return;

        bool isNew;
        lock (_inputLock)
            isNew = _keysDown.Add(key); // false when this is an auto-repeat

        if (isNew)
        {
            bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
            bool caps = Keyboard.IsKeyToggled(Key.CapsLock);
            _keyEvents.Enqueue(new KeyEvent(true, (uint)key, CharFor(key, shift, caps), Modifiers()));
        }

        e.Handled = true;
    }

    /// <summary>The typed character for a key (US layout) — needed for text prompts like the
    /// manual-lookup copy-protection screens. 0 for non-printable keys.</summary>
    private static uint CharFor(DosKey key, bool shift, bool caps)
    {
        if (key is >= DosKey.A and <= DosKey.Z)
        {
            char c = (char)('a' + (key - DosKey.A));
            return shift ^ caps ? char.ToUpperInvariant(c) : c;
        }

        if (key is >= DosKey.Keypad0 and <= DosKey.Keypad9)
            return (uint)('0' + (key - DosKey.Keypad0));

        if (key is >= DosKey.D0 and <= DosKey.D9)
        {
            const string digits = "0123456789";
            const string shifted = ")!@#$%^&*(";
            int i = key - DosKey.D0;
            return shift ? shifted[i] : (uint)digits[i];
        }

        return key switch
        {
            DosKey.Space => ' ',
            DosKey.Enter => '\r',
            DosKey.Tab => '\t',
            DosKey.Backspace => 8,
            DosKey.Minus => shift ? '_' : (uint)'-',
            DosKey.Equals => shift ? '+' : (uint)'=',
            DosKey.Comma => shift ? '<' : (uint)',',
            DosKey.Period => shift ? '>' : (uint)'.',
            DosKey.Slash => shift ? '?' : (uint)'/',
            DosKey.Semicolon => shift ? ':' : (uint)';',
            DosKey.Apostrophe => shift ? '"' : (uint)'\'',
            DosKey.LeftBracket => shift ? '{' : (uint)'[',
            DosKey.RightBracket => shift ? '}' : (uint)']',
            DosKey.Backslash => shift ? '|' : (uint)'\\',
            DosKey.Backquote => shift ? '~' : (uint)'`',
            _ => 0,
        };
    }

    private void OnKeyUp(object sender, KeyEventArgs e)
    {
        var key = KeyMap.ToDosKey(e.Key == Key.System ? e.SystemKey : e.Key);
        if (key == DosKey.None)
            return;

        bool wasDown;
        lock (_inputLock)
            wasDown = _keysDown.Remove(key);

        if (wasDown)
            _keyEvents.Enqueue(new KeyEvent(false, (uint)key, 0, Modifiers()));
        e.Handled = true;
    }

    private static ushort Modifiers()
    {
        ushort m = 0;
        var mod = Keyboard.Modifiers;
        if (mod.HasFlag(ModifierKeys.Shift)) m |= (ushort)KeyModifier.Shift;
        if (mod.HasFlag(ModifierKeys.Control)) m |= (ushort)KeyModifier.Ctrl;
        if (mod.HasFlag(ModifierKeys.Alt)) m |= (ushort)KeyModifier.Alt;
        return m;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        var pos = e.GetPosition(this);

        // Locked (FPS) mode: feed the delta from centre, then warp the cursor back so it can
        // never hit the window edge — infinite turning, like a captured mouse.
        if (_mouseLocked)
        {
            double dx = pos.X - ActualWidth / 2, dy = pos.Y - ActualHeight / 2;
            if (dx != 0 || dy != 0)
            {
                lock (_inputLock)
                {
                    _mouseAccumX += dx * _mouseScale;
                    _mouseAccumY += dy * _mouseScale;
                }
                WarpToCentre();
            }
            return;
        }

        if (_lastMouse is { } last)
        {
            lock (_inputLock)
            {
                _mouseAccumX += (pos.X - last.X) * _mouseScale;
                _mouseAccumY += (pos.Y - last.Y) * _mouseScale;
            }
        }

        _lastMouse = pos;
    }

    private void OnMouseButton(object sender, MouseButtonEventArgs e)
    {
        // Middle button is the host lock toggle (not forwarded to the game).
        if (e.ChangedButton == MouseButton.Middle)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                ToggleMouseLock();
            e.Handled = true;
            return;
        }

        bool down = e.ButtonState == MouseButtonState.Pressed;
        lock (_inputLock)
        {
            switch (e.ChangedButton)
            {
                case MouseButton.Left: _mouseLeft = down; break;
                case MouseButton.Right: _mouseRight = down; break;
            }
        }

        Focus();
    }

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        // DOS games don't read the wheel, so it's free for adjusting mouse sensitivity.
        _sensitivity = Math.Clamp(_sensitivity + (e.Delta > 0 ? 0.25 : -0.25), 0.25, 6.0);
        _mouseScale = _dpiScale * _sensitivity;
        ShowHint($"Mouse sensitivity {_sensitivity:0.00}×");
        e.Handled = true;
    }

    private void ToggleMouseLock()
    {
        _mouseLocked = !_mouseLocked;
        if (_mouseLocked)
        {
            Cursor = Cursors.None;
            CaptureMouse();
            WarpToCentre();
            ShowHint("Mouse locked — middle-click to release");
        }
        else
        {
            ReleaseMouseCapture();
            Cursor = Cursors.Arrow;
            ShowHint("Mouse unlocked");
        }
    }

    private void WarpToCentre()
    {
        var screen = PointToScreen(new Point(ActualWidth / 2, ActualHeight / 2));
        SetCursorPos((int)screen.X, (int)screen.Y);
    }

    private void ShowHint(string text, double seconds = 1.3)
    {
        Hint.Text = text;
        Hint.Visibility = Visibility.Visible;
        if (_hintTimer is null)
        {
            _hintTimer = new DispatcherTimer();
            _hintTimer.Tick += (_, _) => { _hintTimer!.Stop(); Hint.Visibility = Visibility.Collapsed; };
        }
        _hintTimer.Interval = TimeSpan.FromSeconds(seconds);
        _hintTimer.Stop();
        _hintTimer.Start();
    }

    [System.Runtime.InteropServices.LibraryImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static partial bool SetCursorPos(int x, int y);

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        SaveGameState();
        _lcdTimer?.Stop();
        _lcdWindow?.Close();
        _session.Stop();
        _session.Dispose();
        _audioOut?.Stop();
        _audioOut?.Dispose();
        _audioOut = null;
        _audioBuffer = null;
    }

    // Persist the window size so next launch restores it. (The remembered executable is set only
    // by an explicit Run-menu choice, so a plain launch never overwrites it here.)
    private void SaveGameState()
    {
        try
        {
            var size = WindowState == WindowState.Normal
                ? new Size(ActualWidth, ActualHeight)
                : RestoreBounds.Size;

            var store = ((App)Application.Current).Services.Store;
            var state = store.ReadState(_instance.GameboxPath) with
            {
                WindowWidth = (int)size.Width,
                WindowHeight = (int)size.Height,
            };
            store.WriteState(_instance.GameboxPath, state);
        }
        catch
        {
            // State is a convenience; never let saving it block closing the game.
        }
    }
}
