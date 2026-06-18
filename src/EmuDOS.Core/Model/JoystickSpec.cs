namespace EmuDOS.Core.Model;

/// <summary>
/// Joystick configuration. The <see cref="Type"/> sets the emulated gameport so the game
/// sees the joystick it was written for. (Pad→keyboard binding for keyboard-only games is a
/// planned addition handled at the input layer.)
/// </summary>
public sealed record JoystickSpec
{
    public JoystickType Type { get; init; } = JoystickType.Auto;
}
