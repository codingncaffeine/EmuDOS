namespace EmuDOS.Core.Model;

/// <summary>
/// How a game's box is shown on the shelf. <see cref="Default"/> follows the global
/// <c>UserSettings.Use3DBoxes</c> preference; <see cref="TwoD"/>/<see cref="ThreeD"/> override it
/// per game (for when one style's art is poor for a particular title).
/// </summary>
public enum BoxStyle
{
    Default,
    TwoD,
    ThreeD,
}

/// <summary>Per-game hardware-3dfx (OpenGL Voodoo) choice. <see cref="Default"/> follows the global
/// <c>UserSettings.Hardware3dfx</c>; the others override it per game.</summary>
public enum Hardware3dfxMode
{
    Default,
    On,
    Off,
}

/// <summary>What the user dropped in to create the gamebox.</summary>
public enum SourceMediaType
{
    Folder,
    Zip,
    Iso,
    Floppy,
}

/// <summary>Emulated machine / video adapter. Maps to dosbox_pure <c>machine</c>.</summary>
public enum MachineType
{
    Svga,
    Vga,
    Ega,
    Cga,
    Tandy,
    Hercules,
    PcJr,
}

/// <summary>SVGA chipset when <see cref="MachineType.Svga"/>. Maps to dosbox_pure <c>svga</c>.</summary>
public enum SvgaChipset
{
    S3Trio64,
    Et3000,
    Et4000,
    Paradise,
    VesaNoLfb,
    VesaOldVbe,
}

/// <summary>CPU model. Maps to dosbox_pure <c>cpu_type</c> (restart-required).</summary>
public enum CpuType
{
    Auto,
    I386,
    I386Slow,
    I386Prefetch,
    I486Slow,
    PentiumSlow,
    PentiumMmx,
}

/// <summary>Recompiler core. Maps to dosbox_pure <c>cpu_core</c>.</summary>
public enum CpuCore
{
    Auto,
    Dynamic,
    Normal,
    Simple,
}

/// <summary>
/// How emulated speed is chosen. <see cref="Fixed"/> carries an exact cycle count in
/// <see cref="CpuSpec.FixedCycles"/>; because dosbox_pure only exposes preset cycle
/// values, a non-preset exact count is applied via a generated DOSBOX.BAT.
/// </summary>
public enum CyclesMode
{
    Auto,
    Max,
    Fixed,
}

/// <summary>Sound Blaster model. Maps to dosbox_pure <c>sblaster_type</c> (restart-required).</summary>
public enum SoundBlasterType
{
    Sb16,
    SbPro2,
    SbPro1,
    Sb2,
    Sb1,
    GameBlaster,
    None,
}

/// <summary>OPL/AdLib emulation mode. Maps to dosbox_pure <c>sblaster_adlib_mode</c>.</summary>
public enum AdlibMode
{
    Auto,
    Cms,
    Opl2,
    DualOpl2,
    Opl3,
    Opl3Gold,
    None,
}

/// <summary>MIDI output device. SoundFont/MT-32 require the ROM/SF2 in the engine system dir.</summary>
public enum MidiDevice
{
    None,
    GeneralMidi,
    Mt32,
    SoundFont,
}

/// <summary>Kind of media mounted on a drive letter.</summary>
public enum MountKind
{
    Hdd,
    Cd,
    Floppy,
}

/// <summary>
/// The gameport joystick type a game expects. DOS games read the analog gameport, not the
/// physical controller — getting this right per game is what makes a modern pad "just work"
/// as the joystick the game supports. Maps to DOSBox <c>[joystick] joysticktype</c>.
/// </summary>
public enum JoystickType
{
    /// <summary>Let DOSBox decide (its default).</summary>
    Auto,

    /// <summary>No joystick.</summary>
    None,

    /// <summary>Standard 2-axis, 2-button stick.</summary>
    TwoAxis,

    /// <summary>4-axis stick.</summary>
    FourAxis,

    /// <summary>Second 4-axis profile (dual sticks).</summary>
    FourAxis2,

    /// <summary>Thrustmaster Flight Control System.</summary>
    Fcs,

    /// <summary>CH Flightstick Pro.</summary>
    Ch,
}

/// <summary>
/// Where a profile's values came from, for the curated-base ← user-override layering.
/// </summary>
public enum ProfileOrigin
{
    /// <summary>Built-in safe defaults; no curated match was found.</summary>
    Default,

    /// <summary>Resolved from the shipped curated catalog.</summary>
    CuratedBase,

    /// <summary>The user edited the effective profile.</summary>
    UserOverride,
}
