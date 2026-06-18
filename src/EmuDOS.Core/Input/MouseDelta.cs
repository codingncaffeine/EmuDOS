namespace EmuDOS.Core.Input;

/// <summary>
/// Relative mouse movement since the last poll, plus current button state. DOS games read
/// the mouse as relative motion, so X/Y are deltas (consumed each poll), not absolute.
/// </summary>
public readonly record struct MouseDelta(int X, int Y, bool Left, bool Right, bool Middle)
{
    public static MouseDelta None => default;
}
