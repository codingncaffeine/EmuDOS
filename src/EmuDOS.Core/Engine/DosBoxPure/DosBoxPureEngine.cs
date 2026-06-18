using EmuDOS.Core.Model;

namespace EmuDOS.Core.Engine.DosBoxPure;

/// <summary>
/// The dosbox_pure libretro engine. Knows where the core DLL and system directory live;
/// produces a <see cref="DosBoxPureSession"/> per game.
/// </summary>
public sealed class DosBoxPureEngine : IDosEngine
{
    private readonly string _corePath;
    private readonly string _systemDirectory;

    /// <param name="corePath">Full path to <c>dosbox_pure_libretro.dll</c>.</param>
    /// <param name="systemDirectory">Directory holding SoundFonts / MT-32 ROMs / BIOS.</param>
    public DosBoxPureEngine(string corePath, string systemDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(corePath);
        _corePath = corePath;
        _systemDirectory = systemDirectory ?? string.Empty;
    }

    public string Id => "dosbox_pure";

    public string DisplayName => "DOSBox Pure";

    public EngineCapabilities Capabilities => new()
    {
        EngineId = Id,
        SaveStates = true,
        Reset = true,
        ExactCycles = true,     // via generated DOSBOX.BAT
        HardwareRendered = false,
    };

    public IDosSession CreateSession(GameInstance instance, IEngineHost host)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(host);
        return new DosBoxPureSession(instance, host, _corePath, _systemDirectory);
    }
}
