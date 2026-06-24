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
    private byte[] _frameBuffer = [];     // stable displayed frame (native or shaded); lock-guarded with the dims
    private byte[] _nativeBuffer = [];    // pre-shader frame, emu-thread only (shader input)
    private int _frameWidth;
    private int _frameHeight;
    private WriteableBitmap? _bitmap;
    private int _renderQueued;

    private WaveOutEvent? _audioOut;
    private BufferedWaveProvider? _audioBuffer;
    private byte[] _audioBytes = [];
    private int _audioBatches;
    private int _sampleRate = 48000;

    private Core.Media.RecordingService? _recorder;
    private int _recWidth, _recHeight;

    private readonly Key _screenshotKey;
    private readonly Key _recordKey;
    private readonly Key? _mouseLockKey;
    private readonly Key _menuKey;
    private readonly Key _saveStateKey;
    private readonly Key _loadStateKey;
    private readonly Key _cheatKey;
    private readonly Key _fastForwardKey;
    private readonly Key _slowMotionKey;
    private readonly Key _pauseKey;
    private readonly Key _rewindKey;
    private readonly Key _shaderCycleKey;
    // Slang CRT shader (librashader). _desiredPreset (UI) is a slang relative path, "" = off; the emu
    // thread syncs _activePreset and owns the D3D11 ShaderRenderer.
    private volatile string _desiredPreset = "";
    private string _activePreset = "\0"; // sentinel forces the first sync
    private Effects.Librashader.ShaderRenderer? _shaderRenderer;
    private System.Collections.Generic.List<string>? _presetCycle;
    private bool _isPaused;
    private volatile bool _menuHeld; // mapped to the gamepad L3 button, which opens dosbox's menu

    private static Key ParseKey(string name, Key fallback) => Enum.TryParse<Key>(name, out var k) ? k : fallback;

    private readonly byte[]? _lut; // brightness/gamma lookup; null = no adjustment (fast path)

    private Mt32LcdWindow? _lcdWindow;
    private DispatcherTimer? _lcdTimer;

    private readonly GameInstance _instance;
    private readonly AppLog _log;
    private readonly long _gameId;                       // library id, for play-time accrual
    private readonly DateTime _sessionStart = DateTime.UtcNow;

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
    private DispatcherTimer? _hintTimer;
    private Point? _lastMouse;

    public EmulatorWindow(IDosEngine engine, GameInstance instance, long gameId = 0, byte[]? initialState = null)
    {
        InitializeComponent();
        DarkChrome.Apply(this);
        WindowIcon.Apply(this); // force the taskbar (big) icon
        _instance = instance;
        _gameId = gameId;
        Title = $"EmuDOS — {instance.Profile.Title}";

        var settings = ((App)Application.Current).Services.Settings;
        _screenshotKey = ParseKey(settings.ScreenshotKey, Key.F12);
        _recordKey = ParseKey(settings.RecordKey, Key.F9);
        _mouseLockKey = Enum.TryParse<Key>(settings.MouseLockKey, out var mk) ? mk : null;
        _menuKey = ParseKey(settings.MenuKey, Key.F10);
        _saveStateKey = ParseKey(settings.SaveStateKey, Key.F5);
        _loadStateKey = ParseKey(settings.LoadStateKey, Key.F8);
        _cheatKey = ParseKey(settings.CheatKey, Key.F11);
        _fastForwardKey = ParseKey(settings.FastForwardKey, Key.F6);
        _slowMotionKey = ParseKey(settings.SlowMotionKey, Key.F7);
        _pauseKey = ParseKey(settings.PauseKey, Key.Pause);
        _rewindKey = ParseKey(settings.RewindKey, Key.F4);
        _shaderCycleKey = ParseKey(settings.ShaderCycleKey, Key.F3);
        // Per-game slang shader preset (relative path under the downloaded pack), "" = none. Set by the
        // shader-cycle key and saved per game. Old WPF-shader values (e.g. "Crt") just won't resolve.
        _desiredPreset = ((App)Application.Current).Services.Store.ReadState(_instance.GameboxPath).Shader ?? "";
        // Holding fast-forward/slow-motion/rewind across a focus change would otherwise stick; reset it.
        Deactivated += (_, _) => { _session?.SetSpeed(1.0); _session?.SetRewinding(false); };

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
        if (initialState is not null)
            _session.SetInitialState(initialState); // restore this save state once the game has booted
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

        // Build the native frame into _nativeBuffer (emu thread only — no lock needed; this thread is
        // serial and is the sole writer/reader of _nativeBuffer).
        int needed = w * h * 4;
        if (_nativeBuffer.Length < needed)
            _nativeBuffer = new byte[needed];
        if (frame.Format == CorePixelFormat.Xrgb8888)
            CopyXrgb8888(frame, w, h);
        else
            CopyRgb565(frame, w, h);
        ApplyLut(w * h);

        // Slang shader (librashader): GPU pass on this thread. The final frame (shaded or native) is
        // copied into _frameBuffer with its dims under one lock, so the display/record/screenshot paths
        // always see a consistent buffer+size (no native↔shaded flicker, no recording size race).
        SyncShaderPreset();
        byte[] final = _nativeBuffer;
        int fw = w, fh = h;
        if (_shaderRenderer is { IsReady: true })
        {
            var shaded = _shaderRenderer.Process(_nativeBuffer, w, h, w * 4, isBgr32: true, out int sw, out int sh);
            if (shaded != null && sw > 0 && sh > 0) { final = shaded; fw = sw; fh = sh; }
        }

        lock (_frameLock)
        {
            int need = fw * fh * 4;
            if (_frameBuffer.Length < need)
                _frameBuffer = new byte[need];
            Buffer.BlockCopy(final, 0, _frameBuffer, 0, need);
            _frameWidth = fw;
            _frameHeight = fh;
        }

        if (Interlocked.CompareExchange(ref _renderQueued, 1, 0) == 0)
            Dispatcher.BeginInvoke(RenderFrame);
    }

    // Apply the per-game preset change (emu thread): (re)build the D3D11 librashader chain. "" = off.
    private void SyncShaderPreset()
    {
        if (_desiredPreset == _activePreset)
            return;
        _activePreset = _desiredPreset;
        _shaderRenderer?.Dispose();
        _shaderRenderer = null;
        if (string.IsNullOrEmpty(_desiredPreset))
            return;

        var paths = ((App)Application.Current).Services.Paths;
        var presetPath = Effects.Librashader.ShaderCatalog.Resolve(paths.SlangShaderRoot, _desiredPreset);
        if (presetPath is null)
        {
            _log.Info($"[shader] preset not found: {_desiredPreset}");
            return;
        }
        var r = new Effects.Librashader.ShaderRenderer();
        if (r.Initialize(paths.LibrashaderDllPath, presetPath))
            _shaderRenderer = r;
        else
        {
            _log.Info($"[shader] init failed ({_desiredPreset}): {r.LastError}");
            r.Dispose();
        }
    }

    public void OnCoreLog(int level, string message)
    {
        var tag = level switch { 0 => "DBG", 1 => "INFO", 2 => "WARN", 3 => "ERR", _ => "LOG" };
        _log.Info($"[core:{tag}] {message}");

        // dosbox reports the running program ("[DOSBOX STATUS] Program: NAME - …"). A program that
        // starts right after the shell (COMMAND/DOSBOX) is one the *user* launched from the prompt —
        // i.e. what they ran to play. Programs it then chains to aren't (prev isn't the shell). We
        // remember the last such launch so "ran it once from DOS" sticks.
        const string marker = "Program: ";
        int i = message.IndexOf(marker, StringComparison.Ordinal);
        if (i < 0)
            return;
        int start = i + marker.Length;
        int dash = message.IndexOf(" -", start, StringComparison.Ordinal);
        var name = (dash > start ? message[start..dash] : message[start..]).Trim();
        if (name.Length == 0)
            return;

        bool isShell = name.Equals("COMMAND", StringComparison.OrdinalIgnoreCase) || name.Equals("DOSBOX", StringComparison.OrdinalIgnoreCase);
        bool prevWasShell = _prevProgram is null
            || _prevProgram.Equals("COMMAND", StringComparison.OrdinalIgnoreCase)
            || _prevProgram.Equals("DOSBOX", StringComparison.OrdinalIgnoreCase);
        if (prevWasShell && !isShell)
            _lastLaunch = name;
        _prevProgram = name;
    }

    private string? _prevProgram;
    private string? _lastLaunch;

    // Map the program the user launched to a content executable (relative, DOS-style), skipping
    // setup tools and DOS extenders (those are never what "play the game" means).
    private string? CapturedLaunch()
    {
        if (_lastLaunch is null)
            return null;
        try
        {
            string[] exts = [".exe", ".com", ".bat"];
            var match = System.IO.Directory.EnumerateFiles(_instance.ContentPath, "*.*", System.IO.SearchOption.AllDirectories)
                .FirstOrDefault(f => exts.Contains(System.IO.Path.GetExtension(f).ToLowerInvariant())
                                  && System.IO.Path.GetFileNameWithoutExtension(f).Equals(_lastLaunch, StringComparison.OrdinalIgnoreCase));
            if (match is null || Core.Import.DosExecutables.IsRuntimeHelper(match))
                return null;
            var name = System.IO.Path.GetFileNameWithoutExtension(match).ToLowerInvariant();
            if (name.Contains("setup") || name.Contains("install") || name.Contains("config"))
                return null;
            return System.IO.Path.GetRelativePath(_instance.ContentPath, match).Replace('/', '\\');
        }
        catch { return null; }
    }

    public void SetAudioSampleRate(int sampleRate)
    {
        _sampleRate = sampleRate;
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
        _recorder?.WriteAudio(_audioBytes, source.Length);

        if (++_audioBatches % 300 == 1)
            _log.Info($"audio #{_audioBatches} buffered={buffer.BufferedBytes}B | input: {_session.InputDiagnostics}");
    }

    private void CopyXrgb8888(in VideoFrame frame, int w, int h)
    {
        // XRGB8888 little-endian is byte order B,G,R,X — identical to WPF Bgr32.
        for (int y = 0; y < h; y++)
            Marshal.Copy(frame.Data + (y * frame.Pitch), _nativeBuffer, y * w * 4, w * 4);
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
                _nativeBuffer[dst++] = (byte)((b << 3) | (b >> 2));
                _nativeBuffer[dst++] = (byte)((g << 2) | (g >> 4));
                _nativeBuffer[dst++] = (byte)((r << 3) | (r >> 2));
                _nativeBuffer[dst++] = 0;
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

        var buf = _nativeBuffer;
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

            // Recording: feed the raw native frame (FFmpeg nearest-upscales to the displayed size).
            var recorder = _recorder;
            if (recorder?.IsRecording == true && _frameWidth == _recWidth && _frameHeight == _recHeight)
                recorder.WriteVideoFrame(_frameBuffer, _frameWidth * _frameHeight * 4);
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
            // Middle button is the mouse-lock toggle (handled separately), never forwarded to the game.
            return new MouseDelta(dx, dy, _mouseLeft, _mouseRight, false);
        }
    }

    public bool IsButtonDown(int port, PadButton button) => button == PadButton.L3 && _menuHeld;

    // ── Input events ──────────────────────────────────────────────────────────

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        // System keys (F10, Alt combos) arrive as Key.System with the real key in SystemKey;
        // compare hotkeys against the unwrapped key or an F10 binding never matches.
        var effective = e.Key == Key.System ? e.SystemKey : e.Key;

        // Ctrl+V types clipboard text into DOS (Boxer-style paste); the game never sees the shortcut.
        if (effective == Key.V && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            PasteFromClipboard();
            e.Handled = true;
            return;
        }

        // Bound hotkeys are handled here and not forwarded to the game.
        if (effective == _screenshotKey)
        {
            CaptureScreenshot();
            e.Handled = true;
            return;
        }
        if (effective == _recordKey)
        {
            ToggleRecording();
            e.Handled = true;
            return;
        }
        if (_mouseLockKey is { } mouseLockKey && effective == mouseLockKey)
        {
            ToggleMouseLock();
            e.Handled = true;
            return;
        }
        if (effective == _menuKey)
        {
            // Held = the L3 button (see IsButtonDown), which opens dosbox's menu — where CDs/disks
            // are swapped. Lets you change the inserted disc from inside a booted OS.
            _menuHeld = true;
            e.Handled = true;
            return;
        }
        if (effective == _saveStateKey)
        {
            QuickSaveState();
            e.Handled = true;
            return;
        }
        if (effective == _loadStateKey)
        {
            QuickLoadState();
            e.Handled = true;
            return;
        }
        if (effective == _cheatKey)
        {
            OpenCheats();
            e.Handled = true;
            return;
        }
        if (effective == _fastForwardKey)
        {
            _session.SetSpeed(4.0); // hold to fast-forward
            e.Handled = true;
            return;
        }
        if (effective == _slowMotionKey)
        {
            _session.SetSpeed(0.5); // hold to slow down
            e.Handled = true;
            return;
        }
        if (effective == _rewindKey)
        {
            _session.SetRewinding(true); // hold to rewind
            e.Handled = true;
            return;
        }
        if (effective == _shaderCycleKey)
        {
            if (!e.IsRepeat)
                CycleShader();
            e.Handled = true;
            return;
        }
        if (effective == _pauseKey)
        {
            if (!e.IsRepeat)
                TogglePause();
            e.Handled = true;
            return;
        }

        var key = KeyMap.ToDosKey(effective);
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
        var effective = e.Key == Key.System ? e.SystemKey : e.Key;

        if (effective == _menuKey)
        {
            _menuHeld = false;
            e.Handled = true;
            return;
        }
        if (effective == _fastForwardKey || effective == _slowMotionKey)
        {
            _session.SetSpeed(1.0); // release returns to normal speed
            e.Handled = true;
            return;
        }
        if (effective == _rewindKey)
        {
            _session.SetRewinding(false); // release stops rewinding
            e.Handled = true;
            return;
        }

        var key = KeyMap.ToDosKey(effective);
        if (key == DosKey.None)
            return;

        bool wasDown;
        lock (_inputLock)
            wasDown = _keysDown.Remove(key);

        if (wasDown)
            _keyEvents.Enqueue(new KeyEvent(false, (uint)key, 0, Modifiers()));
        e.Handled = true;
    }

    /// <summary>Type clipboard text into DOS as paced keystrokes (Boxer-style paste). Bound to Ctrl+V.</summary>
    private void PasteFromClipboard()
    {
        string text;
        try { text = Clipboard.GetText(); }
        catch { return; }
        if (string.IsNullOrEmpty(text))
            return;
        if (text.Length > 4096)
            text = text[..4096]; // commands/serials are short — cap to avoid flooding the BIOS buffer

        var chars = new Queue<char>(text);
        DosKey? holding = null;
        ushort holdMods = 0;
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(25) };
        timer.Tick += (_, _) =>
        {
            // Release the previously pressed key one tick after pressing it — a clean, paced tap.
            if (holding is { } h)
            {
                _keyEvents.Enqueue(new KeyEvent(false, (uint)h, 0, holdMods));
                holding = null;
                return;
            }
            if (chars.Count == 0)
            {
                timer.Stop();
                return;
            }
            var mapped = CharToKey(chars.Dequeue());
            if (mapped is null)
                return; // unmappable character — skip it and continue next tick
            var (key, shift) = mapped.Value;
            holdMods = shift ? (ushort)KeyModifier.Shift : (ushort)0;
            _keyEvents.Enqueue(new KeyEvent(true, (uint)key, CharFor(key, shift, false), holdMods));
            holding = key;
        };
        timer.Start();
    }

    // Map a character to the DOS key (and whether Shift is needed) that produces it (US layout).
    private static (DosKey Key, bool Shift)? CharToKey(char c)
    {
        if (c is >= 'a' and <= 'z') return (DosKey.A + (c - 'a'), false);
        if (c is >= 'A' and <= 'Z') return (DosKey.A + (c - 'A'), true);
        if (c is >= '0' and <= '9') return (DosKey.D0 + (c - '0'), false);
        return c switch
        {
            ' ' => (DosKey.Space, false),
            '\n' or '\r' => (DosKey.Enter, false),
            '\t' => (DosKey.Tab, false),
            '-' => (DosKey.Minus, false), '_' => (DosKey.Minus, true),
            '=' => (DosKey.Equals, false), '+' => (DosKey.Equals, true),
            ',' => (DosKey.Comma, false), '<' => (DosKey.Comma, true),
            '.' => (DosKey.Period, false), '>' => (DosKey.Period, true),
            '/' => (DosKey.Slash, false), '?' => (DosKey.Slash, true),
            ';' => (DosKey.Semicolon, false), ':' => (DosKey.Semicolon, true),
            '\'' => (DosKey.Apostrophe, false), '"' => (DosKey.Apostrophe, true),
            '[' => (DosKey.LeftBracket, false), '{' => (DosKey.LeftBracket, true),
            ']' => (DosKey.RightBracket, false), '}' => (DosKey.RightBracket, true),
            '\\' => (DosKey.Backslash, false), '|' => (DosKey.Backslash, true),
            '`' => (DosKey.Backquote, false), '~' => (DosKey.Backquote, true),
            ')' => (DosKey.D0, true), '!' => (DosKey.D1, true), '@' => (DosKey.D2, true),
            '#' => (DosKey.D3, true), '$' => (DosKey.D4, true), '%' => (DosKey.D5, true),
            '^' => (DosKey.D6, true), '&' => (DosKey.D7, true), '*' => (DosKey.D8, true),
            '(' => (DosKey.D9, true),
            _ => null,
        };
    }

    // Cycle the CRT shader live: Off -> each downloaded "crt" slang preset. Persisted per game; the
    // emu thread rebuilds the librashader chain on the next frame (see SyncShaderPreset).
    private void CycleShader()
    {
        var paths = ((App)Application.Current).Services.Paths;
        _presetCycle ??= BuildPresetCycle(paths.SlangShaderRoot);
        if (_presetCycle.Count <= 1)
        {
            ShowHint("No CRT shaders — download them in Preferences → Downloads", 2.2);
            return;
        }
        int idx = _presetCycle.FindIndex(p => string.Equals(p, _desiredPreset, StringComparison.OrdinalIgnoreCase));
        _desiredPreset = _presetCycle[(idx + 1) % _presetCycle.Count];
        var name = _desiredPreset.Length == 0 ? "Off" : System.IO.Path.GetFileNameWithoutExtension(_desiredPreset);
        ShowHint($"Shader: {name}", 1.4);
        var store = ((App)Application.Current).Services.Store;
        store.WriteState(_instance.GameboxPath, store.ReadState(_instance.GameboxPath) with { Shader = _desiredPreset });
    }

    // Off + the "crt" category of the downloaded slang pack (empty list when nothing's installed).
    private static System.Collections.Generic.List<string> BuildPresetCycle(string slangRoot)
    {
        var list = new System.Collections.Generic.List<string> { "" }; // Off
        foreach (var p in Effects.Librashader.ShaderCatalog.GetDownloaded(slangRoot))
            if (p.Category.Equals("crt", StringComparison.OrdinalIgnoreCase))
                list.Add(p.RelativePath);
        return list;
    }

    private void TogglePause()
    {
        _isPaused = !_isPaused;
        if (_isPaused)
            _session.Pause();
        else
            _session.Resume();
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
            // Locked motion is fed by raw input (RawInputHook / WM_INPUT) — true physical deltas that
            // never clip at a screen edge. Here we just keep the hidden cursor parked near centre so
            // its clicks stay over the window.
            WarpToCentre();
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
        // Warp to the centre of the CONTENT area (matches the locked-mouse delta measurement), and
        // round rather than truncate so there's no sub-pixel directional bias each frame.
        var root = (FrameworkElement)Content;
        var screen = root.PointToScreen(new Point(root.ActualWidth / 2, root.ActualHeight / 2));
        SetCursorPos((int)Math.Round(screen.X), (int)Math.Round(screen.Y));
    }

    // Capture the current frame to a PNG — at the game's native resolution, or the displayed window
    // size, per Preferences → Media.
    private void CaptureScreenshot()
    {
        if (_bitmap is null)
            return;
        try
        {
            // The frame buffer already carries the shader (librashader runs before present), so a clone
            // of the displayed bitmap is the shaded image at its rendered resolution.
            BitmapSource source = _bitmap.Clone();
            source.Freeze();

            var dir = new Core.Library.Gamebox(_instance.GameboxPath).ScreenshotsDir;
            System.IO.Directory.CreateDirectory(dir);
            var path = System.IO.Path.Combine(dir, $"{SafeName(_instance.Profile.Title)} {DateTime.Now:yyyy-MM-dd HH-mm-ss}.png");

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));
            using (var fs = System.IO.File.Create(path))
                encoder.Save(fs);

            ShowHint("Screenshot saved", 1.0);
        }
        catch (Exception ex)
        {
            _log.Info($"Screenshot failed: {ex.Message}");
            ShowHint("Screenshot failed");
        }
    }

    // F9: start/stop video recording. Stop encodes on a background thread so the UI doesn't freeze.
    private void ToggleRecording()
    {
        var services = ((App)Application.Current).Services;

        if (_recorder?.IsRecording == true)
        {
            var recorder = _recorder;
            _recorder = null;
            RecIndicator.Visibility = Visibility.Collapsed;
            ShowHint("Encoding video…", 2.0);
            System.Threading.Tasks.Task.Run(() =>
            {
                var path = recorder.Stop();
                Dispatcher.Invoke(() => ShowHint(path is not null ? "Video saved" : "Recording failed"));
            });
            return;
        }

        if (_frameWidth <= 0)
            return;

        var ffmpeg = services.Downloads.InstalledPath(Core.Downloads.AssetManifest.Ffmpeg);
        if (!System.IO.File.Exists(ffmpeg))
        {
            ShowHint("Install FFmpeg first (Downloads tab)", 2.5);
            return;
        }

        var dir = new Core.Library.Gamebox(_instance.GameboxPath).VideosDir;
        System.IO.Directory.CreateDirectory(dir);
        var path2 = System.IO.Path.Combine(dir, $"{SafeName(_instance.Profile.Title)} {DateTime.Now:yyyy-MM-dd HH-mm-ss}.mp4");

        _recorder = new Core.Media.RecordingService(ffmpeg);
        int dispW = (int)Screen.ActualWidth & ~1, dispH = (int)Screen.ActualHeight & ~1; // even dims for the encoder
        // Record the raw native frames; FFmpeg nearest-upscales to the displayed size. (Baking a CRT
        // shader into video is left to the GPU/OpenGL channel — the software-shader-per-frame capture
        // froze the app.)
        _recWidth = _frameWidth;
        _recHeight = _frameHeight;
        var error = _recorder.Start(path2, _recWidth, _recHeight, _sampleRate, services.Settings.VideoQuality, dispW, dispH);
        if (error is not null)
        {
            _recorder = null;
            ShowHint($"Couldn't record: {error}", 2.5);
            return;
        }
        RecIndicator.Visibility = Visibility.Visible;
        ShowHint("Recording started — F9 to stop", 1.5);
    }

    private CheatWindow? _cheatWindow;

    // F11 — open the cheat engine over the running game (a free-floating window so it can live on a
    // second monitor). Reuse the existing one if it's already up.
    private void OpenCheats()
    {
        if (_cheatWindow is not null)
        {
            _cheatWindow.Activate();
            return;
        }
        _cheatWindow = new CheatWindow(_session) { Owner = this };
        _cheatWindow.Closed += (_, _) => _cheatWindow = null;
        _cheatWindow.Show();
        ShowHint("Cheat engine opened (F11)");
    }

    private static string SafeName(string title) =>
        string.Concat(title.Select(c => System.IO.Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim();

    // Quick save/load use slot 0; the session writes state0.sav into the gamebox's saves folder.
    // F5 writes a NEW save state each time (never overwrites); F8 loads the newest.
    private void QuickSaveState()
    {
        var data = _session.SaveStateBytes();
        if (data is null)
        {
            ShowHint("Save state failed");
            return;
        }
        try
        {
            Core.Library.SaveStateStore.Write(_instance.SavePath, data, ThumbnailPng());
            ShowHint("Save state written");
        }
        catch (Exception ex)
        {
            _log.Info($"Save state write failed: {ex.Message}");
            ShowHint("Save state failed");
        }
    }

    private void QuickLoadState()
    {
        var newest = Core.Library.SaveStateStore.Newest(_instance.SavePath);
        var data = newest is null ? null : Core.Library.SaveStateStore.ReadState(newest);
        if (data is null)
        {
            ShowHint("No save state to load");
            return;
        }
        ShowHint(_session.LoadStateBytes(data) ? "Save state loaded" : "Load state failed");
    }

    // A small PNG thumbnail of the current frame for a save state (best-effort; null on failure).
    private byte[]? ThumbnailPng()
    {
        try
        {
            if (_bitmap is null)
                return null;
            const int targetW = 320;
            var snap = _bitmap.Clone();
            snap.Freeze();
            double scale = Math.Min(1.0, targetW / (double)snap.PixelWidth);
            BitmapSource src = scale < 1.0
                ? new TransformedBitmap(snap, new System.Windows.Media.ScaleTransform(scale, scale))
                : snap;
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(src));
            using var ms = new System.IO.MemoryStream();
            encoder.Save(ms);
            return ms.ToArray();
        }
        catch
        {
            return null;
        }
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

    // Raw relative mouse input (WM_INPUT) for locked/FPS mode: reads the physical device delta, so
    // it never loses motion when the OS clips the cursor at a screen edge (the old warp-to-centre
    // delta did, biasing vertical motion). Unlocked mode keeps using absolute positions.
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct RawInputDevice { public ushort UsagePage; public ushort Usage; public uint Flags; public nint Target; }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct RawInputHeader { public uint Type; public uint Size; public nint Device; public nint WParam; }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct RawMouse { public ushort Flags; public uint Buttons; public uint RawButtons; public int LastX; public int LastY; public uint Extra; }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct RawInput { public RawInputHeader Header; public RawMouse Mouse; }

    [System.Runtime.InteropServices.LibraryImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static partial bool RegisterRawInputDevices([System.Runtime.InteropServices.In] RawInputDevice[] devices, uint num, uint size);

    [System.Runtime.InteropServices.LibraryImport("user32.dll")]
    private static partial uint GetRawInputData(nint rawInput, uint command, nint data, ref uint size, uint headerSize);

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        var devices = new[] { new RawInputDevice { UsagePage = 0x01, Usage = 0x02, Flags = 0, Target = hwnd } };
        RegisterRawInputDevices(devices, 1, (uint)System.Runtime.InteropServices.Marshal.SizeOf<RawInputDevice>());
        System.Windows.Interop.HwndSource.FromHwnd(hwnd)?.AddHook(RawInputHook);
    }

    private nint RawInputHook(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        const int WM_INPUT = 0x00FF;
        const uint RID_INPUT = 0x10000003;
        if (msg != WM_INPUT || !_mouseLocked)
            return 0;

        uint headerSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<RawInputHeader>();
        uint size = 0;
        GetRawInputData(lParam, RID_INPUT, 0, ref size, headerSize);
        if (size == 0 || size > 1024)
            return 0;

        var buffer = System.Runtime.InteropServices.Marshal.AllocHGlobal((int)size);
        try
        {
            if (GetRawInputData(lParam, RID_INPUT, buffer, ref size, headerSize) == size)
            {
                var raw = System.Runtime.InteropServices.Marshal.PtrToStructure<RawInput>(buffer);
                if (raw.Header.Type == 0 && (raw.Mouse.Flags & 1) == 0) // type 0 = mouse, bit0 clear = relative
                {
                    lock (_inputLock)
                    {
                        _mouseAccumX += raw.Mouse.LastX * _sensitivity;
                        _mouseAccumY += raw.Mouse.LastY * _sensitivity;
                    }
                }
            }
        }
        finally
        {
            System.Runtime.InteropServices.Marshal.FreeHGlobal(buffer);
        }
        return 0;
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_recorder?.IsRecording == true)
        {
            ShowHint("Finishing video…");
            _recorder.Stop(); // finalize the recording before we tear the session down
            _recorder = null;
        }

        SaveGameState();
        try
        {
            var seconds = (int)(DateTime.UtcNow - _sessionStart).TotalSeconds;
            ((App)Application.Current).Services.Library.AddPlayTime(_gameId, seconds);
        }
        catch { /* play-time is a convenience; never block closing */ }
        _lcdTimer?.Stop();
        _lcdWindow?.Close();
        _session.Stop();
        _session.Dispose();
        _shaderRenderer?.Dispose(); // emu thread has stopped — safe to release the D3D11 chain here
        _shaderRenderer = null;
        _audioOut?.Stop();
        _audioOut?.Dispose();
        _audioOut = null;
        _audioBuffer = null;
    }

    // Persist the window size so next launch restores it. (The launch program is chosen by auto-
    // detection or the program picker, not from whatever happened to run, so we don't touch it here.)
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

            var launched = CapturedLaunch();
            if (launched is not null)
                state = state with { LastRunProgram = launched };

            store.WriteState(_instance.GameboxPath, state);
        }
        catch
        {
            // State is a convenience; never let saving it block closing the game.
        }
    }
}
