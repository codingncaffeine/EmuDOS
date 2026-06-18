namespace EmuDOS.Core.Input;

/// <summary>
/// Virtual gamepad buttons, engine-neutral. Values equal the libretro
/// <c>RETRO_DEVICE_ID_JOYPAD_*</c> ids so the session can translate with a cast. The host
/// produces these from XInput (or any pad backend); the dosbox_pure engine routes them to
/// the emulated gameport joystick or a pad→keyboard binding.
/// </summary>
public enum PadButton
{
    B = 0,
    Y = 1,
    Select = 2,
    Start = 3,
    Up = 4,
    Down = 5,
    Left = 6,
    Right = 7,
    A = 8,
    X = 9,
    L = 10,
    R = 11,
    L2 = 12,
    R2 = 13,
    L3 = 14,
    R3 = 15,
}
