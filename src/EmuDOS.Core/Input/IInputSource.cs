namespace EmuDOS.Core.Input;

/// <summary>
/// The current input state, provided by the host (keyboard + mouse + gamepad). The engine
/// queries this each frame and translates it to whatever the emulator expects. Keeping it
/// engine-neutral is what lets the host source input from WPF, XInput, etc. without the
/// Core knowing anything about those.
/// </summary>
public interface IInputSource
{
    /// <summary>Is a keyboard key currently held?</summary>
    bool IsKeyDown(DosKey key);

    /// <summary>
    /// Take the next queued key transition (press/release). The engine drains these on its own
    /// thread each frame and feeds them to the core's keyboard callback. False when empty.
    /// </summary>
    bool TryDequeueKey(out KeyEvent keyEvent);

    /// <summary>
    /// Take the mouse movement accumulated since the previous call (relative deltas) plus
    /// current buttons. Called once per frame; deltas are consumed.
    /// </summary>
    MouseDelta PollMouse();

    /// <summary>Is a gamepad button held on the given player port (0-based)?</summary>
    bool IsButtonDown(int port, PadButton button);
}
