using System.Runtime.InteropServices;

namespace EmuDOS.Core.Input;

/// <summary>
/// Reads Xbox-compatible game controllers via XInput (built into Windows — no download needed) and
/// maps them to libretro joypad buttons. <see cref="PadButton"/> values already equal the libretro
/// button ids, so the mapping is a direct bit table. Up to four ports; the left stick doubles as a
/// d-pad and triggers act as L2/R2. <see cref="Poll"/> latches a snapshot once per frame on the
/// emulation thread; <see cref="IsButtonDown"/> reads it.
/// </summary>
public sealed class XInputController
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Gamepad
    {
        public ushort Buttons;
        public byte LeftTrigger, RightTrigger;
        public short ThumbLX, ThumbLY, ThumbRX, ThumbRY;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct State
    {
        public uint PacketNumber;
        public Gamepad Pad;
    }

    private delegate uint GetStateFn(uint userIndex, out State state);

    // XInput button bitmask values.
    private const ushort DpadUp = 0x0001, DpadDown = 0x0002, DpadLeft = 0x0004, DpadRight = 0x0008;
    private const ushort Start = 0x0010, Back = 0x0020, LThumb = 0x0040, RThumb = 0x0080;
    private const ushort LShoulder = 0x0100, RShoulder = 0x0200;
    private const ushort BtnA = 0x1000, BtnB = 0x2000, BtnX = 0x4000, BtnY = 0x8000;

    private const short StickDeadzone = 8000;   // ~25% of 32767
    private const byte TriggerThreshold = 64;    // out of 255

    private readonly GetStateFn? _getState;
    private readonly uint[] _snapshot = new uint[4]; // per-port PadButton bitmask, latched on Poll
    private readonly bool[] _connected = new bool[4];

    public XInputController()
    {
        foreach (var dll in new[] { "xinput1_4.dll", "xinput1_3.dll", "xinput9_1_0.dll" })
        {
            if (NativeLibrary.TryLoad(dll, out var handle) &&
                NativeLibrary.TryGetExport(handle, "XInputGetState", out var proc))
            {
                _getState = Marshal.GetDelegateForFunctionPointer<GetStateFn>(proc);
                break;
            }
        }
    }

    /// <summary>True if XInput is available (it ships with Windows, so effectively always).</summary>
    public bool Available => _getState is not null;

    /// <summary>Refresh the per-port button snapshot. Call once per frame on the emulation thread.</summary>
    public void Poll()
    {
        if (_getState is null)
            return;
        for (uint port = 0; port < 4; port++)
        {
            bool ok = _getState(port, out var state) == 0; // 0 = ERROR_SUCCESS
            _connected[port] = ok;
            _snapshot[port] = ok ? Map(state.Pad) : 0;
        }
    }

    public bool IsButtonDown(int port, PadButton button) =>
        port is >= 0 and < 4 && (_snapshot[port] & (1u << (int)button)) != 0;

    /// <summary>Whether a controller is connected on this port (refreshed by <see cref="Poll"/>).</summary>
    public bool IsConnected(int port) => port is >= 0 and < 4 && _connected[port];

    private static uint Map(Gamepad g)
    {
        uint bits = 0;
        void Set(PadButton b) => bits |= 1u << (int)b;

        // Face buttons — note Xbox A (south) is libretro B, Xbox B is libretro A.
        if ((g.Buttons & BtnA) != 0) Set(PadButton.B);
        if ((g.Buttons & BtnB) != 0) Set(PadButton.A);
        if ((g.Buttons & BtnX) != 0) Set(PadButton.Y);
        if ((g.Buttons & BtnY) != 0) Set(PadButton.X);

        if ((g.Buttons & Start) != 0) Set(PadButton.Start);
        if ((g.Buttons & Back) != 0) Set(PadButton.Select);
        if ((g.Buttons & LShoulder) != 0) Set(PadButton.L);
        if ((g.Buttons & RShoulder) != 0) Set(PadButton.R);
        if ((g.Buttons & LThumb) != 0) Set(PadButton.L3);
        if ((g.Buttons & RThumb) != 0) Set(PadButton.R3);

        if ((g.Buttons & DpadUp) != 0) Set(PadButton.Up);
        if ((g.Buttons & DpadDown) != 0) Set(PadButton.Down);
        if ((g.Buttons & DpadLeft) != 0) Set(PadButton.Left);
        if ((g.Buttons & DpadRight) != 0) Set(PadButton.Right);

        if (g.LeftTrigger > TriggerThreshold) Set(PadButton.L2);
        if (g.RightTrigger > TriggerThreshold) Set(PadButton.R2);

        // Left stick doubles as the d-pad (dosbox_pure reads the joypad digitally).
        if (g.ThumbLY > StickDeadzone) Set(PadButton.Up);
        if (g.ThumbLY < -StickDeadzone) Set(PadButton.Down);
        if (g.ThumbLX < -StickDeadzone) Set(PadButton.Left);
        if (g.ThumbLX > StickDeadzone) Set(PadButton.Right);

        return bits;
    }
}
