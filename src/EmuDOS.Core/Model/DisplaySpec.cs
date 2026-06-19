namespace EmuDOS.Core.Model;

/// <summary>
/// Frontend presentation tweaks applied by EmuDOS to the rendered frame (not dosbox_pure core
/// options — the core doesn't expose these). 1.0 means "leave unchanged".
/// </summary>
public sealed record DisplaySpec
{
    /// <summary>Overall brightness multiplier. 1.0 = unchanged; &gt;1 brighter, &lt;1 darker.</summary>
    public double Brightness { get; init; } = 1.0;

    /// <summary>Gamma. 1.0 = unchanged; &gt;1 lifts midtones (good for too-dark DOS games).</summary>
    public double Gamma { get; init; } = 1.0;
}
