namespace EmuDOS.Core.Input;

/// <summary>
/// A single keyboard transition pushed to a core's keyboard callback. <see cref="KeyCode"/> is a
/// libretro RETROK_* value (i.e. a <see cref="DosKey"/>); <see cref="Modifiers"/> is a RETROKMOD
/// bitmask; <see cref="Character"/> is the typed unicode char (0 when not applicable).
/// </summary>
public readonly record struct KeyEvent(bool Down, uint KeyCode, uint Character, ushort Modifiers);

/// <summary>RETROKMOD_* modifier bits.</summary>
[Flags]
public enum KeyModifier : ushort
{
    None = 0,
    Shift = 0x01,
    Ctrl = 0x02,
    Alt = 0x04,
    Meta = 0x08,
    NumLock = 0x10,
    CapsLock = 0x20,
    ScrollLock = 0x40,
}
