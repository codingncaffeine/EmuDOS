using System.Collections.Concurrent;
using System.Diagnostics;
using EmuDOS.Core.Input;
using EmuDOS.Core.Libretro;
using EmuDOS.Core.Model;

namespace EmuDOS.Core.Engine.DosBoxPure;

/// <summary>
/// A running dosbox_pure game. Owns a dedicated thread that creates the libretro core,
/// drops the generated DOSBOX.BAT into the content, loads the game, and pumps the run loop —
/// forwarding video/audio to the host and translating the host's neutral input to libretro.
/// </summary>
public sealed class DosBoxPureSession : IDosSession
{
    // libretro device classes and ids.
    private const uint DeviceJoypad = 1;
    private const uint DeviceMouse = 2;
    private const uint DeviceKeyboard = 3;
    private const uint MouseX = 0, MouseY = 1, MouseLeft = 2, MouseRight = 3, MouseMiddle = 6;

    private readonly GameInstance _instance;
    private readonly IEngineHost _host;
    private readonly string _corePath;
    private readonly string _systemDir;
    private readonly bool _hardware3dfx;
    private readonly ConcurrentQueue<Action> _pending = new();
    private volatile IReadOnlyDictionary<ulong, byte[]>? _frozen; // cheat freeze set (swapped wholesale)
    private byte[]? _initialState; // a save state to restore once the game has booted (launch-into-state)
    private volatile int _speedPermil = 1000; // run speed ×1000 (1000 = normal); fast-forward / slow-motion

    // Rewind: a ring of recent serialized states, captured + replayed on the core thread only.
    private const int RewindIntervalFrames = 12;             // ~5 snapshots/sec at 60fps
    private const long RewindMaxBytes = 256L * 1024 * 1024;  // cap the buffer (states can be MBs each)
    private readonly LinkedList<byte[]> _rewindBuffer = new();
    private long _rewindBytes;
    private volatile bool _rewinding;

    private Thread? _thread;
    private LibretroCore? _core;
    private volatile bool _running;
    private volatile bool _paused;
    private MouseDelta _mouse;
    private volatile EngineState _state = EngineState.Idle;

    public DosBoxPureSession(GameInstance instance, IEngineHost host, string corePath, string systemDir,
        bool hardware3dfx = true)
    {
        _instance = instance;
        _host = host;
        _corePath = corePath;
        _systemDir = systemDir;
        _hardware3dfx = hardware3dfx;
    }

    public GameInstance Instance => _instance;

    public EngineState State => _state;

    public long FramesPresented => _core?.FrameCount ?? 0;

    /// <summary>Details of the exception that faulted the session, if any (for diagnostics).</summary>
    public string? LastError { get; private set; }

    public event Action<EngineState>? StateChanged;

    public void Start()
    {
        if (_thread is not null)
            return;
        _running = true;
        _thread = new Thread(RunLoop) { Name = "dosbox_pure", IsBackground = true };
        _thread.Start();
    }

    public void Pause()
    {
        if (_state == EngineState.Running)
        {
            _paused = true;
            SetState(EngineState.Paused);
        }
    }

    public void Resume()
    {
        if (_state == EngineState.Paused)
        {
            _paused = false;
            SetState(EngineState.Running);
        }
    }

    public void Reset() => RunOnCoreThread(() => { _core?.Reset(); return true; });

    public void Stop() => _running = false;

    public byte[]? SaveStateBytes()
    {
        byte[]? data = null;
        RunOnCoreThread(() => (data = _core?.SaveState()) is not null);
        return data;
    }

    public bool LoadStateBytes(byte[] data) =>
        RunOnCoreThread(() => _core is not null && _core.LoadState(data));

    // ── Cheat engine memory access (marshalled onto the core thread). ──

    public IReadOnlyList<MemoryRegion> MemoryRegions => _core?.MemoryRegions ?? Array.Empty<MemoryRegion>();

    public IReadOnlyList<(MemoryRegion Region, byte[] Data)> SnapshotMemory()
    {
        IReadOnlyList<(MemoryRegion, byte[])> snap = Array.Empty<(MemoryRegion, byte[])>();
        RunOnCoreThread(() => { snap = _core?.SnapshotMemory() ?? snap; return true; });
        return snap;
    }

    public byte[]? ReadMemory(ulong address, int count)
    {
        if (count <= 0)
            return null;
        var buf = new byte[count];
        return RunOnCoreThread(() => _core is not null && _core.ReadMemory(address, buf, count)) ? buf : null;
    }

    public bool WriteMemory(ulong address, byte[] data) =>
        RunOnCoreThread(() => _core is not null && _core.WriteMemory(address, data));

    /// <summary>Swap in a fresh frozen set; it's re-applied every frame on the core thread.</summary>
    public void SetFrozen(IReadOnlyDictionary<ulong, byte[]>? frozen) => _frozen = frozen;

    /// <summary>Restore this save state shortly after launch (set before <see cref="Start"/>).</summary>
    public void SetInitialState(byte[] state) => _initialState = state;

    /// <summary>Set the run-speed multiplier (1.0 = normal; &gt;1 fast-forward, &lt;1 slow-motion).</summary>
    public void SetSpeed(double multiplier) => _speedPermil = Math.Clamp((int)(multiplier * 1000), 100, 16000);

    /// <summary>Hold to rewind: replay the captured snapshot ring backwards.</summary>
    public void SetRewinding(bool on) => _rewinding = on;

    // Snapshot the current state into the rewind ring, dropping the oldest to stay under the byte cap.
    private void CaptureRewindSnapshot()
    {
        var snap = _core!.SaveState();
        if (snap is null)
            return;
        _rewindBuffer.AddLast(snap);
        _rewindBytes += snap.Length;
        while (_rewindBytes > RewindMaxBytes && _rewindBuffer.First is { } oldest)
        {
            _rewindBytes -= oldest.Value.Length;
            _rewindBuffer.RemoveFirst();
        }
    }

    public void Dispose()
    {
        _running = false;
        _thread?.Join(TimeSpan.FromSeconds(3));
        _thread = null;
    }

    private void RunLoop()
    {
        try
        {
            _core = new LibretroCore(_corePath)
            {
                CoreLog = _host.OnCoreLog,
                SystemDirectory = _systemDir,
                SaveDirectory = _instance.SavePath,
            };

            var plan = DosBoxPureAdapter.BuildLaunchPlan(_instance.Profile, _instance.ContentPath);
            Directory.CreateDirectory(_instance.ContentPath);

            string loadTarget;
            if (_instance.Profile.SourceMedia == SourceMediaType.Iso)
            {
                // Load the disc(s) via an .m3u8 playlist: dosbox_pure then mounts a CD image as D:
                // (a CD loaded directly becomes the C: boot drive instead). A CD on D: is what makes
                // the core detect a bootable disc and leaves C: free/writable — so the start menu
                // offers "[Boot and Install New Operating System]" and the install has somewhere to go.
                // Every disc in the box is listed, so extra discs (e.g. game CDs added to an installed
                // Windows machine) show up in the core's disc-swap menu to mount as D: inside the OS.
                var discs = Directory.EnumerateFiles(_instance.ContentPath)
                    .Where(f => f.EndsWith(".iso", StringComparison.OrdinalIgnoreCase)
                             || f.EndsWith(".cue", StringComparison.OrdinalIgnoreCase)
                             || f.EndsWith(".chd", StringComparison.OrdinalIgnoreCase))
                    .Select(Path.GetFileName)
                    .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (discs.Count > 0)
                {
                    var m3u = Path.Combine(_instance.ContentPath, "emudos.m3u8");
                    File.WriteAllText(m3u, string.Join("\n", discs) + "\n");
                    loadTarget = m3u;
                }
                else
                {
                    loadTarget = _instance.ContentPath;
                }
            }
            else
            {
                File.WriteAllText(Path.Combine(_instance.ContentPath, "DOSBOX.BAT"), plan.AutoexecBat);
                loadTarget = _instance.ContentPath;
            }

            // Hardware-OpenGL 3dfx/Voodoo: turn on the core's hardware-OpenGL Voodoo so it requests
            // SET_HW_RENDER, and accept it. Non-3dfx games keep using the software video callback.
            if (_hardware3dfx)
            {
                _core.Options = new Dictionary<string, string>(plan.CoreOptions)
                {
                    ["dosbox_pure_voodoo"] = "8mb",
                    ["dosbox_pure_voodoo_perf"] = "4", // Hardware OpenGL
                };
                _core.HardwareRender = true;
            }
            else
            {
                _core.Options = plan.CoreOptions;
            }

            _core.Video = (data, w, h, pitch, fmt) =>
                _host.SubmitVideoFrame(new VideoFrame(data, w, h, pitch, fmt));
            _core.InputPoll = PollInput;
            _core.Input = QueryInput;

            // MT-32: when the profile selects it and the ROMs are present, drive our own synth
            // (the core routes MIDI to us via "frontend"), mixing its audio with the core's.
            if (_instance.Profile.Sound.Midi == MidiDevice.Mt32)
                _synth = Audio.Mt32Synth.TryCreate(
                    Path.Combine(_systemDir, "MT32_CONTROL.ROM"),
                    Path.Combine(_systemDir, "MT32_PCM.ROM"));

            if (_synth is not null)
            {
                _core.Audio = MixMt32;
                _core.MidiByte = _synth.FeedByte;
                SierraSoundConfig.EnsureMt32(_instance.ContentPath); // Sierra: point soundDrv at MT-32
            }
            else
            {
                _core.Audio = _host.SubmitAudioFrames;
                _core.MidiByte = _midi.Feed;
            }

            _core.SetCallbacks();
            _core.Init();

            if (!_core.LoadGame(loadTarget))
            {
                Fault();
                return;
            }

            var avInfo = _core.GetAvInfo();
            int fpsLock = _instance.Profile.Machine.FpsLock;
            _host.OnCoreLog(1, $"[fps] frame-rate lock {(fpsLock > 0 ? fpsLock + " requested" : "off")}; "
                + $"core reports {avInfo.Fps:0.##} fps");
            if (_core.HwActive)
                _core.HwPrepareAndReset(avInfo.MaxWidth > 0 ? avInfo.MaxWidth : 1024,
                                        avInfo.MaxHeight > 0 ? avInfo.MaxHeight : 768);
            _host.SetAudioSampleRate((int)Math.Round(avInfo.SampleRate > 1 ? avInfo.SampleRate : 48000));
            PumpFrames(avInfo);
        }
        catch (Exception ex)
        {
            LastError = ex.ToString();
            Fault();
        }
        finally
        {
            DrainPending();
            _core?.Dispose();
            _core = null;
            _synth?.Dispose();
            _synth = null;
            if (_state != EngineState.Faulted)
                SetState(EngineState.Stopped);
        }
    }

    private void PumpFrames(RetroAvInfo av)
    {
        double fps = av.Fps > 1 ? av.Fps : 60.0;
        double frameMs = 1000.0 / fps;
        SetState(EngineState.Running);

        // Baseline wall-clock pacing. Audio-buffer-driven pacing (drain buffered ms after
        // each Run) is the correct long-term approach and is wired in once we validate
        // against a live core + the host's audio sink.
        var sw = Stopwatch.StartNew();
        long frame = 0;
        double targetMs = 0; // accumulated target wall-clock; advances by the speed-scaled frame time

        // Measured output-FPS logging (confirms a frame-rate lock is actually in effect).
        var fpsClock = Stopwatch.StartNew();
        long lastFpsFrame = _core!.FrameCount;
        int fpsLockForLog = _instance.Profile.Machine.FpsLock;

        while (_running)
        {
            DrainPending();
            if (_paused)
            {
                Thread.Sleep(8);
                sw.Restart();
                frame = 0;
                targetMs = 0;
                continue;
            }

            if (_rewinding)
            {
                // Step back through the snapshot ring (don't advance the game or capture).
                if (_rewindBuffer.Last is { } back)
                {
                    try { _core!.LoadState(back.Value); } catch { /* skip a bad snapshot */ }
                    _rewindBytes -= back.Value.Length;
                    _rewindBuffer.RemoveLast();
                }
            }
            else
            {
                _core!.Run();
                frame++;

                // Restore a launch-into-state save once the core's serialize is ready and the game has
                // begun booting (the snapshot replaces the full machine, jumping straight to the save).
                if (_initialState is { } st0 && frame == 30)
                {
                    try { _core.LoadState(st0); } catch { /* size mismatch etc. — just run from boot */ }
                    _initialState = null;
                }

                // Re-apply frozen cheat values after the game updated them this frame.
                if (_frozen is { Count: > 0 } frozen)
                    foreach (var kv in frozen)
                        _core.WriteMemory(kv.Key, kv.Value);

                // Periodically snapshot so the player can rewind.
                if (frame % RewindIntervalFrames == 0)
                    CaptureRewindSnapshot();
            }

            // Advance the target clock by the speed-scaled frame time (fast-forward shrinks it, slow-
            // motion grows it), then sleep to hit it. Resync if we fall badly behind.
            targetMs += frameMs * 1000.0 / _speedPermil;
            double behindMs = sw.Elapsed.TotalMilliseconds - targetMs;
            if (behindMs < -1)
                Thread.Sleep((int)-behindMs);
            else if (behindMs > 250)
            {
                sw.Restart();
                frame = 0;
                targetMs = 0;
            }

            if (fpsClock.ElapsedMilliseconds >= 5000)
            {
                long now = _core!.FrameCount;
                double measured = (now - lastFpsFrame) * 1000.0 / fpsClock.Elapsed.TotalMilliseconds;
                _host.OnCoreLog(1, $"[fps] output {measured:0.#} fps{(fpsLockForLog > 0 ? $" (lock {fpsLockForLog})" : "")}");
                lastFpsFrame = now;
                fpsClock.Restart();
            }
        }

        _rewindBuffer.Clear(); // free the snapshot ring on teardown
        _rewindBytes = 0;
    }

    // Runs on the core thread, once per frame, before input is read: latch the mouse and push
    // any queued key transitions into the core's keyboard callback.
    private const int MinKeyHoldFrames = 4;
    private readonly Dictionary<uint, int> _keyHoldFrames = new();
    private readonly Dictionary<uint, KeyEvent> _pendingUps = new();

    // Runs on the emulation thread, once per frame (single producer for the core's lock-free
    // event queue). Keys are held down for a few frames before their release is delivered, so a
    // game polling the keyboard in a tight loop (copy-protection screens) can't miss a quick tap.
    private void PollInput()
    {
        _gamepad.Poll(); // latch a controller snapshot once per frame (read by QueryInput's joypad case)
        _mouse = _host.Input.PollMouse();

        while (_host.Input.TryDequeueKey(out var key))
        {
            if (key.Down)
            {
                if (_pendingUps.Remove(key.KeyCode, out var prior)) // close a still-pending release first
                    _core?.SendKeyEvent(false, prior.KeyCode, prior.Character, prior.Modifiers);
                _core?.SendKeyEvent(true, key.KeyCode, key.Character, key.Modifiers);
                Interlocked.Increment(ref _keysSent);
                _keyHoldFrames[key.KeyCode] = MinKeyHoldFrames;
            }
            else
            {
                _pendingUps[key.KeyCode] = key; // defer the release until the key has been held a few frames
            }
        }

        foreach (var code in _keyHoldFrames.Keys.ToList())
            if (--_keyHoldFrames[code] <= 0)
                _keyHoldFrames.Remove(code);

        foreach (var (code, up) in _pendingUps.ToList())
            if (!_keyHoldFrames.ContainsKey(code))
            {
                _core?.SendKeyEvent(false, up.KeyCode, up.Character, up.Modifiers);
                _pendingUps.Remove(code);
            }
    }

    private readonly Input.XInputController _gamepad = new();
    private readonly Audio.MidiMonitor _midi = new();
    private Audio.Mt32Synth? _synth;
    private short[] _mt32Buf = [];
    private long _kbQueries, _kbHits, _padQueries, _mouseQueries, _otherQueries, _keysSent;

    /// <summary>The MT-32 LCD text when our synth is active; null otherwise.</summary>
    public string? Mt32Lcd => _synth?.Lcd;

    // Render our MT-32 music and mix it into the core's audio, then forward to the host. Runs on
    // the engine thread, same as FeedByte — so the synth is touched single-threaded.
    private void MixMt32(ReadOnlySpan<short> core)
    {
        int n = core.Length;
        if (_mt32Buf.Length < n)
            _mt32Buf = new short[n];

        _synth!.Render(_mt32Buf, n / 2);
        for (int i = 0; i < n; i++)
        {
            int s = core[i] + _mt32Buf[i];
            _mt32Buf[i] = (short)(s > 32767 ? 32767 : s < -32768 ? -32768 : s);
        }

        _host.SubmitAudioFrames(_mt32Buf.AsSpan(0, n));
    }

    public string InputDiagnostics =>
        $"keysSent={Interlocked.Read(ref _keysSent)} kbQ={Interlocked.Read(ref _kbQueries)} "
        + $"padQ={Interlocked.Read(ref _padQueries)} mouseQ={Interlocked.Read(ref _mouseQueries)} "
        + $"otherQ={Interlocked.Read(ref _otherQueries)} | midiBytes={_synth?.BytesFed ?? _midi.ByteCount} lcd='{_synth?.Lcd ?? _midi.Lcd}' synth={(_synth is null ? "off" : _synth.SampleRate + "Hz")} {_synth?.SysexInfo}";

    private short QueryInput(uint port, uint device, uint index, uint id)
    {
        switch (device)
        {
            case DeviceKeyboard: Interlocked.Increment(ref _kbQueries); break;
            case DeviceJoypad: Interlocked.Increment(ref _padQueries); break;
            case DeviceMouse: Interlocked.Increment(ref _mouseQueries); break;
            default: Interlocked.Increment(ref _otherQueries); break;
        }

        return device switch
        {
            DeviceKeyboard => _host.Input.IsKeyDown((DosKey)id)
                ? Hit()
                : (short)0,
            DeviceJoypad => _gamepad.IsButtonDown((int)port, (PadButton)id)
                            || _host.Input.IsButtonDown((int)port, (PadButton)id)
                ? (short)1
                : (short)0,
            DeviceMouse => id switch
        {
            MouseX => (short)_mouse.X,
            MouseY => (short)_mouse.Y,
            MouseLeft => _mouse.Left ? (short)1 : (short)0,
            MouseRight => _mouse.Right ? (short)1 : (short)0,
            MouseMiddle => _mouse.Middle ? (short)1 : (short)0,
            _ => (short)0,
            },
            _ => (short)0,
        };
    }

    private short Hit()
    {
        Interlocked.Increment(ref _kbHits);
        return 1;
    }

    /// <summary>Queue work to run on the core thread between frames and wait for its result.</summary>
    private bool RunOnCoreThread(Func<bool> action)
    {
        if (!_running || _state is EngineState.Stopped or EngineState.Faulted)
            return false;

        using var done = new ManualResetEventSlim(false);
        bool result = false;
        _pending.Enqueue(() =>
        {
            try { result = action(); }
            catch { result = false; }
            finally { done.Set(); }
        });

        return done.Wait(TimeSpan.FromSeconds(5)) && result;
    }

    private void DrainPending()
    {
        while (_pending.TryDequeue(out var action))
            action();
    }

    private void Fault()
    {
        _running = false;
        SetState(EngineState.Faulted);
    }

    private void SetState(EngineState state)
    {
        _state = state;
        StateChanged?.Invoke(state);
    }
}
