namespace EmuDOS.Core.Libretro;

/// <summary>libretro ABI constants (subset used by EmuDOS's software-rendered host).</summary>
internal static class LibretroConstants
{
    public const uint ApiVersion = 1;

    // retro_environment commands.
    public const uint EnvGetOverscan = 2;
    public const uint EnvGetCanDupe = 3;
    public const uint EnvGetSystemDirectory = 9;
    public const uint EnvSetPixelFormat = 10;
    public const uint EnvSetKeyboardCallback = 12;
    public const uint EnvSetHwRender = 14; // verified vs libretro.h (SET_INPUT_DESCRIPTORS is 11, not this)
    public const uint EnvGetVariable = 15;
    public const uint EnvGetPreferredHwRender = 56;
    public const uint EnvGetHwRenderInterface = 41 | 0x10000; // 41 | RETRO_ENVIRONMENT_EXPERIMENTAL
    public const uint EnvSetVariables = 16;
    public const uint EnvGetVariableUpdate = 17;
    public const uint EnvSetSupportNoGame = 18;
    public const uint EnvGetLogInterface = 27;
    public const uint EnvGetSaveDirectory = 31;
    public const uint EnvGetVfsInterface = 45 | 0x10000; // 45 | RETRO_ENVIRONMENT_EXPERIMENTAL
    public const uint EnvGetMidiInterface = 48 | 0x10000; // 48 | RETRO_ENVIRONMENT_EXPERIMENTAL
    public const uint EnvGetCoreOptionsVersion = 52;
    public const uint EnvSetCoreOptions = 53;
    public const uint EnvSetCoreOptionsIntl = 54;
    public const uint EnvSetCoreOptionsDisplay = 55;
    public const uint EnvSetCoreOptionsV2 = 67;
    public const uint EnvSetCoreOptionsV2Intl = 68;

    // retro HW-render context types (retro_hw_context_type) + the video_refresh sentinel for a HW frame.
    public const uint HwContextNone = 0;
    public const uint HwContextOpenGL = 1;      // compat-profile GL
    public const uint HwContextOpenGLES2 = 2;
    public const uint HwContextOpenGLCore = 3;  // core-profile GL (version in major/minor)
    public const uint HwContextOpenGLES3 = 4;
    public static readonly nint HwFrameBufferValid = -1; // RETRO_HW_FRAME_BUFFER_VALID = (void*)-1

    // retro_pixel_format values (as written by SET_PIXEL_FORMAT).
    public const int PixelFormat0Rgb1555 = 0;
    public const int PixelFormatXrgb8888 = 1;
    public const int PixelFormatRgb565 = 2;

    // retro_memory id.
    public const uint MemorySaveRam = 0;
    public const uint MemorySystemRam = 2;

    // RETRO_ENVIRONMENT_SET_MEMORY_MAPS = (36 | RETRO_ENVIRONMENT_EXPERIMENTAL 0x10000). The core
    // describes its memory regions (live pointers) this way — dosbox_pure uses this, not get_memory_data.
    public const uint EnvSetMemoryMaps = 36 | 0x10000;

    // The dosbox_pure core supports running with content (a folder/zip/conf path).
    public const string DosBoxPureCoreId = "dosbox_pure";
}
