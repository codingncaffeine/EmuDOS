namespace EmuDOS.Core.Model;

/// <summary>
/// Sound hardware settings. The Sound Blaster resource values (<see cref="Port"/>,
/// <see cref="Irq"/>, <see cref="LowDma"/>, <see cref="HighDma"/>) are stored faithfully
/// from the curated source; the engine adapter snaps them to the nearest of
/// dosbox_pure's ten fixed <c>sblaster_conf</c> presets at launch.
/// </summary>
public sealed record SoundSpec
{
    public SoundBlasterType SoundBlaster { get; init; } = SoundBlasterType.Sb16;

    /// <summary>I/O port, e.g. 0x220.</summary>
    public int Port { get; init; } = 0x220;

    public int Irq { get; init; } = 7;

    /// <summary>8-bit DMA channel.</summary>
    public int LowDma { get; init; } = 1;

    /// <summary>16-bit DMA channel.</summary>
    public int HighDma { get; init; } = 5;

    public AdlibMode Adlib { get; init; } = AdlibMode.Auto;

    public bool GravisUltrasound { get; init; }

    public bool TandySound { get; init; }

    public MidiDevice Midi { get; init; } = MidiDevice.None;

    /// <summary>
    /// SoundFont (.sf2) or MT-32 ROM filename. The file must reside in the engine's
    /// system directory; dosbox_pure selects MIDI devices by filename, not full path.
    /// </summary>
    public string? MidiSoundFont { get; init; }

    public int AudioRateHz { get; init; } = 48000;
}
