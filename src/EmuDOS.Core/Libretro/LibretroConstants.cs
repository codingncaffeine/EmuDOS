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
    public const uint EnvSetHwRender = 14;
    public const uint EnvGetVariable = 15;
    public const uint EnvSetVariables = 16;
    public const uint EnvGetVariableUpdate = 17;
    public const uint EnvSetSupportNoGame = 18;
    public const uint EnvGetLogInterface = 27;
    public const uint EnvGetSaveDirectory = 31;
    public const uint EnvGetCoreOptionsVersion = 52;
    public const uint EnvSetCoreOptions = 53;
    public const uint EnvSetCoreOptionsIntl = 54;
    public const uint EnvSetCoreOptionsDisplay = 55;
    public const uint EnvSetCoreOptionsV2 = 67;
    public const uint EnvSetCoreOptionsV2Intl = 68;

    // retro_pixel_format values (as written by SET_PIXEL_FORMAT).
    public const int PixelFormat0Rgb1555 = 0;
    public const int PixelFormatXrgb8888 = 1;
    public const int PixelFormatRgb565 = 2;

    // retro_memory id.
    public const uint MemorySaveRam = 0;
    public const uint MemorySystemRam = 2;

    // The dosbox_pure core supports running with content (a folder/zip/conf path).
    public const string DosBoxPureCoreId = "dosbox_pure";
}
