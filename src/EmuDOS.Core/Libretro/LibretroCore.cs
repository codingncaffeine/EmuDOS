using System.Runtime.InteropServices;
using EmuDOS.Core.Engine;
using static EmuDOS.Core.Libretro.LibretroConstants;

namespace EmuDOS.Core.Libretro;

/// <summary>Receives a decoded video frame. The pointer is valid only for the call.</summary>
public delegate void VideoFrameHandler(nint data, int width, int height, int pitch, PixelFormat format);

/// <summary>Receives interleaved stereo 16-bit PCM produced during the last <c>Run</c>.</summary>
public delegate void AudioFrameHandler(ReadOnlySpan<short> interleavedStereo);

/// <summary>Answers a libretro input query (port, device, index, id) → state.</summary>
public delegate short InputStateProvider(uint port, uint device, uint index, uint id);

/// <summary>
/// A thin, software-rendered libretro host: loads a core DLL (dosbox_pure), answers its
/// environment queries — crucially returning per-game option values from <see cref="Options"/>
/// on <c>GET_VARIABLE</c> — and pumps the run loop, forwarding video/audio to handlers and
/// pulling input from a provider. Not thread-safe; drive it from one owner thread.
/// </summary>
public sealed class LibretroCore : IDisposable
{
    private nint _handle;
    private bool _gameLoaded;
    private nint _gamePathPtr;
    private readonly Dictionary<string, nint> _ansiCache = new(StringComparer.Ordinal);
    private bool _variablesDirty = true;

    // Strong refs to callbacks handed to the core — must outlive the core.
    private readonly RetroEnvironmentDelegate _envCb;
    private readonly RetroVideoRefreshDelegate _videoCb;
    private readonly RetroAudioSampleDelegate _audioCb;
    private readonly RetroAudioSampleBatchDelegate _audioBatchCb;
    private readonly RetroInputPollDelegate _inputPollCb;
    private readonly RetroInputStateDelegate _inputStateCb;
    private RetroKeyboardEventDelegate? _keyboardEvent;

    private readonly RetroMidiInputEnabled _midiInputEnabled;
    private readonly RetroMidiOutputEnabled _midiOutputEnabled;
    private readonly RetroMidiRead _midiRead;
    private readonly RetroMidiWrite _midiWrite;
    private readonly RetroMidiFlush _midiFlush;

    private readonly RetroInit _init;
    private readonly RetroDeinit _deinit;
    private readonly RetroGetSystemInfo _getSystemInfo;
    private readonly RetroGetSystemAvInfo _getSystemAvInfo;
    private readonly RetroSetEnvironment _setEnvironment;
    private readonly RetroSetVideoRefresh _setVideoRefresh;
    private readonly RetroSetAudioSample _setAudioSample;
    private readonly RetroSetAudioSampleBatch _setAudioSampleBatch;
    private readonly RetroSetInputPoll _setInputPoll;
    private readonly RetroSetInputState _setInputState;
    private readonly RetroReset _reset;
    private readonly RetroRun _run;
    private readonly RetroLoadGame _loadGame;
    private readonly RetroUnloadGame _unloadGame;
    private readonly RetroSerializeSize? _serializeSize;
    private readonly RetroSerialize? _serialize;
    private readonly RetroUnserialize? _unserialize;

    public LibretroCore(string corePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(corePath);
        if (!File.Exists(corePath))
            throw new FileNotFoundException("Core not found", corePath);

        _handle = NativeMethods.LoadLibraryEx(corePath, 0, NativeMethods.LOAD_WITH_ALTERED_SEARCH_PATH);
        if (_handle == 0)
        {
            int err = Marshal.GetLastWin32Error();
            throw new InvalidOperationException(
                $"Failed to load core '{corePath}' (Win32 error {err}).");
        }

        _init = Bind<RetroInit>("retro_init");
        _deinit = Bind<RetroDeinit>("retro_deinit");
        _getSystemInfo = Bind<RetroGetSystemInfo>("retro_get_system_info");
        _getSystemAvInfo = Bind<RetroGetSystemAvInfo>("retro_get_system_av_info");
        _setEnvironment = Bind<RetroSetEnvironment>("retro_set_environment");
        _setVideoRefresh = Bind<RetroSetVideoRefresh>("retro_set_video_refresh");
        _setAudioSample = Bind<RetroSetAudioSample>("retro_set_audio_sample");
        _setAudioSampleBatch = Bind<RetroSetAudioSampleBatch>("retro_set_audio_sample_batch");
        _setInputPoll = Bind<RetroSetInputPoll>("retro_set_input_poll");
        _setInputState = Bind<RetroSetInputState>("retro_set_input_state");
        _reset = Bind<RetroReset>("retro_reset");
        _run = Bind<RetroRun>("retro_run");
        _loadGame = Bind<RetroLoadGame>("retro_load_game");
        _unloadGame = Bind<RetroUnloadGame>("retro_unload_game");
        _serializeSize = TryBind<RetroSerializeSize>("retro_serialize_size");
        _serialize = TryBind<RetroSerialize>("retro_serialize");
        _unserialize = TryBind<RetroUnserialize>("retro_unserialize");

        _envCb = Environment;
        _videoCb = OnVideoRefresh;
        _audioCb = OnAudioSample;
        _audioBatchCb = OnAudioBatch;
        _inputPollCb = () => InputPoll?.Invoke();
        _inputStateCb = OnInputState;

        _midiInputEnabled = () => false;
        _midiOutputEnabled = () => true;
        _midiRead = _ => false;
        _midiWrite = (value, _) => { MidiByte?.Invoke(value); return true; };
        _midiFlush = () => true;
    }

    /// <summary>dosbox_pure_* option values returned to the core on <c>GET_VARIABLE</c>.</summary>
    public IReadOnlyDictionary<string, string> Options { get; set; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Directory the core reads system files (BIOS, SoundFonts, MT-32 ROMs) from.</summary>
    public string SystemDirectory { get; set; } = string.Empty;

    /// <summary>Directory the core reads/writes save data to.</summary>
    public string SaveDirectory { get; set; } = string.Empty;

    /// <summary>The pixel format the core negotiated (defaults to dosbox_pure's XRGB8888).</summary>
    public PixelFormat PixelFormat { get; private set; } = PixelFormat.Xrgb8888;

    public VideoFrameHandler? Video { get; set; }

    public AudioFrameHandler? Audio { get; set; }

    public InputStateProvider? Input { get; set; }

    /// <summary>Invoked once per frame, before input is read — latch fresh input here.</summary>
    public Action? InputPoll { get; set; }

    /// <summary>
    /// Push a key event into the core via its keyboard callback (dosbox_pure reads the keyboard
    /// this way). No-op until the core registers a callback. Call on the core thread.
    /// </summary>
    public void SendKeyEvent(bool down, uint keycode, uint character, ushort modifiers) =>
        _keyboardEvent?.Invoke(down, keycode, character, modifiers);

    /// <summary>
    /// Each MIDI byte the core emits when <c>dosbox_pure_midi = "frontend"</c>. We synthesize it
    /// ourselves (our own MT-32) and read the LCD from the stream.
    /// </summary>
    public Action<byte>? MidiByte { get; set; }

    public bool NeedsFullPath { get; private set; }

    /// <summary>Register callbacks (environment first) — call before <see cref="Init"/>.</summary>
    public void SetCallbacks()
    {
        _setEnvironment(_envCb);
        _setVideoRefresh(_videoCb);
        _setAudioSample(_audioCb);
        _setAudioSampleBatch(_audioBatchCb);
        _setInputPoll(_inputPollCb);
        _setInputState(_inputStateCb);
    }

    public void Init()
    {
        _init();
        nint p = Marshal.AllocHGlobal(Marshal.SizeOf<RetroSystemInfo>());
        try
        {
            _getSystemInfo(p);
            var info = Marshal.PtrToStructure<RetroSystemInfo>(p);
            NeedsFullPath = info.need_fullpath;
        }
        finally
        {
            Marshal.FreeHGlobal(p);
        }
    }

    /// <summary>Load content (a folder, .zip, or .conf path for dosbox_pure).</summary>
    public bool LoadGame(string contentPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentPath);

        _gamePathPtr = Marshal.StringToHGlobalAnsi(contentPath);
        var info = new RetroGameInfo { path = _gamePathPtr };
        nint infoPtr = Marshal.AllocHGlobal(Marshal.SizeOf<RetroGameInfo>());
        try
        {
            Marshal.StructureToPtr(info, infoPtr, false);
            _gameLoaded = _loadGame(infoPtr);
            return _gameLoaded;
        }
        finally
        {
            Marshal.FreeHGlobal(infoPtr);
        }
    }

    public void Run() => _run();

    public void Reset() => _reset();

    public RetroAvInfo GetAvInfo()
    {
        nint p = Marshal.AllocHGlobal(Marshal.SizeOf<RetroSystemAvInfo>());
        try
        {
            _getSystemAvInfo(p);
            var av = Marshal.PtrToStructure<RetroSystemAvInfo>(p);
            return new RetroAvInfo
            {
                BaseWidth = (int)av.geometry.base_width,
                BaseHeight = (int)av.geometry.base_height,
                MaxWidth = (int)av.geometry.max_width,
                MaxHeight = (int)av.geometry.max_height,
                AspectRatio = av.geometry.aspect_ratio,
                Fps = av.timing.fps,
                SampleRate = av.timing.sample_rate,
            };
        }
        finally
        {
            Marshal.FreeHGlobal(p);
        }
    }

    /// <summary>Serialize the running game's state, or null if unsupported.</summary>
    public byte[]? SaveState()
    {
        if (_serializeSize is null || _serialize is null)
            return null;

        nuint size = _serializeSize();
        if (size == 0)
            return null;

        nint buf = Marshal.AllocHGlobal((int)size);
        try
        {
            if (!_serialize(buf, size))
                return null;
            var bytes = new byte[(int)size];
            Marshal.Copy(buf, bytes, 0, (int)size);
            return bytes;
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }
    }

    public bool LoadState(byte[] state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (_unserialize is null || state.Length == 0)
            return false;

        nint buf = Marshal.AllocHGlobal(state.Length);
        try
        {
            Marshal.Copy(state, 0, buf, state.Length);
            return _unserialize(buf, (nuint)state.Length);
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }
    }

    private bool Environment(uint cmd, nint data)
    {
        switch (cmd)
        {
            case EnvGetCanDupe:
                if (data != 0) Marshal.WriteByte(data, 1);
                return true;

            case EnvGetOverscan:
                if (data != 0) Marshal.WriteByte(data, 0);
                return true;

            case EnvSetPixelFormat:
                int fmt = Marshal.ReadInt32(data);
                if (fmt == PixelFormatXrgb8888) { PixelFormat = PixelFormat.Xrgb8888; return true; }
                if (fmt == PixelFormatRgb565) { PixelFormat = PixelFormat.Rgb565; return true; }
                return false; // 0RGB1555 unsupported by our presenter

            case EnvGetVariable:
                return HandleGetVariable(data);

            case EnvGetVariableUpdate:
                if (data != 0) Marshal.WriteByte(data, (byte)(_variablesDirty ? 1 : 0));
                _variablesDirty = false;
                return true;

            case EnvGetSystemDirectory:
                Marshal.WriteIntPtr(data, GetCachedAnsi(SystemDirectory));
                return true;

            case EnvGetSaveDirectory:
                Marshal.WriteIntPtr(data, GetCachedAnsi(SaveDirectory));
                return true;

            case EnvGetCoreOptionsVersion:
                if (data != 0) Marshal.WriteInt32(data, 2);
                return true;

            case EnvSetVariables:
            case EnvSetCoreOptions:
            case EnvSetCoreOptionsIntl:
            case EnvSetCoreOptionsV2:
            case EnvSetCoreOptionsV2Intl:
            case EnvSetCoreOptionsDisplay:
            case EnvSetSupportNoGame:
                // Acknowledged; we feed values through GET_VARIABLE regardless of the schema.
                return true;

            case EnvSetKeyboardCallback:
                // The core gives us its retro_keyboard_event_t; we push key events into it.
                if (data != 0)
                {
                    nint fp = Marshal.ReadIntPtr(data);
                    _keyboardEvent = fp != 0
                        ? Marshal.GetDelegateForFunctionPointer<RetroKeyboardEventDelegate>(fp)
                        : null;
                }
                return true;

            case EnvGetMidiInterface:
                // Fill the retro_midi_interface struct with our 5 callbacks so the core routes
                // MIDI ("frontend" driver) to us. Layout: 5 consecutive function pointers.
                if (data != 0)
                {
                    Marshal.WriteIntPtr(data, 0 * nint.Size, Marshal.GetFunctionPointerForDelegate(_midiInputEnabled));
                    Marshal.WriteIntPtr(data, 1 * nint.Size, Marshal.GetFunctionPointerForDelegate(_midiOutputEnabled));
                    Marshal.WriteIntPtr(data, 2 * nint.Size, Marshal.GetFunctionPointerForDelegate(_midiRead));
                    Marshal.WriteIntPtr(data, 3 * nint.Size, Marshal.GetFunctionPointerForDelegate(_midiWrite));
                    Marshal.WriteIntPtr(data, 4 * nint.Size, Marshal.GetFunctionPointerForDelegate(_midiFlush));
                }
                return true;

            case EnvSetHwRender:
                // Force the software path; we don't provide a GL/Vulkan context (yet).
                return false;

            default:
                return false;
        }
    }

    private bool HandleGetVariable(nint data)
    {
        nint keyPtr = Marshal.ReadIntPtr(data, 0);
        string? key = Marshal.PtrToStringAnsi(keyPtr);
        if (key is not null && Options.TryGetValue(key, out var value))
        {
            Marshal.WriteIntPtr(data, nint.Size, GetCachedAnsi(value));
            return true;
        }

        Marshal.WriteIntPtr(data, nint.Size, 0);
        return false;
    }

    private void OnVideoRefresh(nint data, uint width, uint height, nuint pitch)
    {
        if (data == 0)
            return; // duplicated frame
        Video?.Invoke(data, (int)width, (int)height, (int)pitch, PixelFormat);
    }

    private unsafe nuint OnAudioBatch(nint data, nuint frames)
    {
        int shorts = (int)frames * 2;
        if (data != 0 && shorts > 0 && Audio is not null)
            Audio(new ReadOnlySpan<short>((void*)data, shorts));
        return frames;
    }

    private void OnAudioSample(short left, short right)
    {
        if (Audio is null)
            return;
        Span<short> pair = stackalloc short[2];
        pair[0] = left;
        pair[1] = right;
        Audio(pair);
    }

    private short OnInputState(uint port, uint device, uint index, uint id) =>
        Input?.Invoke(port, device, index, id) ?? 0;

    private nint GetCachedAnsi(string value)
    {
        if (!_ansiCache.TryGetValue(value, out var ptr))
        {
            ptr = Marshal.StringToHGlobalAnsi(value);
            _ansiCache[value] = ptr;
        }

        return ptr;
    }

    private T Bind<T>(string name) where T : Delegate
    {
        nint ptr = NativeMethods.GetProcAddress(_handle, name);
        if (ptr == 0)
            throw new InvalidOperationException($"Core export '{name}' not found.");
        return Marshal.GetDelegateForFunctionPointer<T>(ptr);
    }

    private T? TryBind<T>(string name) where T : Delegate
    {
        nint ptr = NativeMethods.GetProcAddress(_handle, name);
        return ptr == 0 ? null : Marshal.GetDelegateForFunctionPointer<T>(ptr);
    }

    public void Dispose()
    {
        if (_gameLoaded)
        {
            try { _unloadGame(); } catch { /* tearing down */ }
            _gameLoaded = false;
        }

        if (_handle != 0)
        {
            try { _deinit(); } catch { /* tearing down */ }
            NativeMethods.FreeLibrary(_handle);
            _handle = 0;
        }

        foreach (var ptr in _ansiCache.Values)
            Marshal.FreeHGlobal(ptr);
        _ansiCache.Clear();

        if (_gamePathPtr != 0)
        {
            Marshal.FreeHGlobal(_gamePathPtr);
            _gamePathPtr = 0;
        }
    }
}
