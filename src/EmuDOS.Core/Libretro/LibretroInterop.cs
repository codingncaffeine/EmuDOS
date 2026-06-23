using System.Runtime.InteropServices;

namespace EmuDOS.Core.Libretro;

// libretro callback delegates (all cdecl). Instances passed to a core must be kept alive
// for the core's lifetime — LibretroCore stores them in fields.

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
[return: MarshalAs(UnmanagedType.I1)]
internal delegate bool RetroEnvironmentDelegate(uint cmd, nint data);

// retro_log_printf_t is variadic: void log(level, fmt, ...). We read the level + format string and
// the first vararg, so the common log(level, "%s", msg) pattern resolves to the actual message.
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate void RetroLogPrintf(int level, nint fmt, nint arg1);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate void RetroVideoRefreshDelegate(nint data, uint width, uint height, nuint pitch);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate void RetroAudioSampleDelegate(short left, short right);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate nuint RetroAudioSampleBatchDelegate(nint data, nuint frames);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate void RetroInputPollDelegate();

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate short RetroInputStateDelegate(uint port, uint device, uint index, uint id);

// retro_keyboard_event_t: the core hands us this via SET_KEYBOARD_CALLBACK and we invoke it to
// push key events in (dosbox_pure reads the keyboard this way, not via input_state polling).
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate void RetroKeyboardEventDelegate(
    [MarshalAs(UnmanagedType.U1)] bool down, uint keycode, uint character, ushort keyModifiers);

// retro_midi_interface callbacks (the frontend implements these; the core calls them to send
// MIDI out when dosbox_pure_midi = "frontend"). RETRO_CALLCONV = cdecl.
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.U1)]
internal delegate bool RetroMidiInputEnabled();
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.U1)]
internal delegate bool RetroMidiOutputEnabled();
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.U1)]
internal delegate bool RetroMidiRead(nint outByte);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.U1)]
internal delegate bool RetroMidiWrite([MarshalAs(UnmanagedType.U1)] byte value, uint deltaTime);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.U1)]
internal delegate bool RetroMidiFlush();

// Exported core entry points.
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void RetroInit();
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void RetroDeinit();
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate uint RetroApiVersion();
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void RetroGetSystemInfo(nint info);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void RetroGetSystemAvInfo(nint info);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void RetroSetEnvironment(RetroEnvironmentDelegate cb);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void RetroSetVideoRefresh(RetroVideoRefreshDelegate cb);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void RetroSetAudioSample(RetroAudioSampleDelegate cb);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void RetroSetAudioSampleBatch(RetroAudioSampleBatchDelegate cb);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void RetroSetInputPoll(RetroInputPollDelegate cb);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void RetroSetInputState(RetroInputStateDelegate cb);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void RetroReset();
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void RetroRun();
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] internal delegate bool RetroLoadGame(nint game);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void RetroUnloadGame();
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate nuint RetroSerializeSize();
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] internal delegate bool RetroSerialize(nint data, nuint size);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] internal delegate bool RetroUnserialize(nint data, nuint size);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void RetroSetControllerPortDevice(uint port, uint device);
// retro_get_memory_data(id) -> void* ; retro_get_memory_size(id) -> size_t. For RETRO_MEMORY_SYSTEM_RAM
// (id 2) dosbox_pure should hand back the live DOSBox memory array — the basis for a cheat engine.
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate nint RetroGetMemoryData(uint id);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate nuint RetroGetMemorySize(uint id);

[StructLayout(LayoutKind.Sequential, Pack = 8)]
internal struct RetroSystemInfo
{
    public nint library_name;
    public nint library_version;
    public nint valid_extensions;
    [MarshalAs(UnmanagedType.I1)] public bool need_fullpath;
    [MarshalAs(UnmanagedType.I1)] public bool block_extract;
}

[StructLayout(LayoutKind.Sequential, Pack = 8)]
internal struct RetroGameGeometry
{
    public uint base_width;
    public uint base_height;
    public uint max_width;
    public uint max_height;
    public float aspect_ratio;
}

[StructLayout(LayoutKind.Sequential, Pack = 8)]
internal struct RetroSystemTiming
{
    public double fps;
    public double sample_rate;
}

[StructLayout(LayoutKind.Sequential, Pack = 8)]
internal struct RetroSystemAvInfo
{
    public RetroGameGeometry geometry;
    public RetroSystemTiming timing;
}

[StructLayout(LayoutKind.Sequential, Pack = 8)]
internal struct RetroGameInfo
{
    public nint path;   // const char*
    public nint data;   // const void*
    public nuint size;  // size_t
    public nint meta;   // const char*
}

[StructLayout(LayoutKind.Sequential, Pack = 8)]
internal struct RetroVariable
{
    public nint key;    // const char*
    public nint value;  // const char*
}
