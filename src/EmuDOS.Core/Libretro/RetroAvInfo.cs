namespace EmuDOS.Core.Libretro;

/// <summary>Audio/video parameters reported by the core after a game loads.</summary>
public sealed record RetroAvInfo
{
    public int BaseWidth { get; init; }
    public int BaseHeight { get; init; }
    public int MaxWidth { get; init; }
    public int MaxHeight { get; init; }
    public float AspectRatio { get; init; }

    /// <summary>Target frames per second of the emulated system.</summary>
    public double Fps { get; init; }

    /// <summary>Audio sample rate in Hz.</summary>
    public double SampleRate { get; init; }
}
