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
    private readonly ConcurrentQueue<Action> _pending = new();

    private Thread? _thread;
    private LibretroCore? _core;
    private volatile bool _running;
    private volatile bool _paused;
    private MouseDelta _mouse;
    private volatile EngineState _state = EngineState.Idle;

    public DosBoxPureSession(GameInstance instance, IEngineHost host, string corePath, string systemDir)
    {
        _instance = instance;
        _host = host;
        _corePath = corePath;
        _systemDir = systemDir;
    }

    public GameInstance Instance => _instance;

    public EngineState State => _state;

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

    public bool SaveState(int slot) => RunOnCoreThread(() => DoSaveState(slot));

    public bool LoadState(int slot) => RunOnCoreThread(() => DoLoadState(slot));

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
                SystemDirectory = _systemDir,
                SaveDirectory = _instance.SavePath,
            };

            var plan = DosBoxPureAdapter.BuildLaunchPlan(_instance.Profile);
            Directory.CreateDirectory(_instance.ContentPath);
            File.WriteAllText(Path.Combine(_instance.ContentPath, "DOSBOX.BAT"), plan.AutoexecBat);
            _core.Options = plan.CoreOptions;

            _core.Video = (data, w, h, pitch, fmt) =>
                _host.SubmitVideoFrame(new VideoFrame(data, w, h, pitch, fmt));
            _core.Audio = _host.SubmitAudioFrames;
            _core.InputPoll = () => _mouse = _host.Input.PollMouse();
            _core.Input = QueryInput;

            _core.SetCallbacks();
            _core.Init();

            if (!_core.LoadGame(_instance.ContentPath))
            {
                Fault();
                return;
            }

            var avInfo = _core.GetAvInfo();
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
        while (_running)
        {
            DrainPending();
            if (_paused)
            {
                Thread.Sleep(8);
                sw.Restart();
                frame = 0;
                continue;
            }

            _core!.Run();
            frame++;

            double behindMs = sw.Elapsed.TotalMilliseconds - (frame * frameMs);
            if (behindMs < -1)
                Thread.Sleep((int)-behindMs);
            else if (behindMs > 250)
            {
                sw.Restart(); // fell far behind; resync rather than fast-forward
                frame = 0;
            }
        }
    }

    private short QueryInput(uint port, uint device, uint index, uint id) => device switch
    {
        DeviceKeyboard => _host.Input.IsKeyDown((DosKey)id) ? (short)1 : (short)0,
        DeviceJoypad => _host.Input.IsButtonDown((int)port, (PadButton)id) ? (short)1 : (short)0,
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

    private bool DoSaveState(int slot)
    {
        var data = _core?.SaveState();
        if (data is null)
            return false;
        Directory.CreateDirectory(_instance.SavePath);
        File.WriteAllBytes(SlotPath(slot), data);
        return true;
    }

    private bool DoLoadState(int slot)
    {
        var path = SlotPath(slot);
        return File.Exists(path) && _core is not null && _core.LoadState(File.ReadAllBytes(path));
    }

    private string SlotPath(int slot) =>
        Path.Combine(_instance.SavePath, $"state{slot}.sav");

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
