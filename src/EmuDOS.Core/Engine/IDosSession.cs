using EmuDOS.Core.Input;
using EmuDOS.Core.Model;

namespace EmuDOS.Core.Engine;

/// <summary>
/// A single running game. Owns the engine's run loop and lifecycle; disposing it tears the
/// emulator down cleanly. Created via <see cref="IDosEngine.CreateSession"/> in the
/// <see cref="EngineState.Idle"/> state.
/// </summary>
public interface IDosSession : IDisposable
{
    GameInstance Instance { get; }

    EngineState State { get; }

    /// <summary>Raised whenever <see cref="State"/> changes. May fire on a background thread.</summary>
    event Action<EngineState>? StateChanged;

    /// <summary>Begin emulation (starts the run loop). No-op if already running.</summary>
    void Start();

    void Pause();

    void Resume();

    /// <summary>Soft-reset the emulated machine.</summary>
    void Reset();

    /// <summary>Stop emulation. The session is finished afterwards and should be disposed.</summary>
    void Stop();

    /// <summary>Serialize the current machine state, or null if unsupported/failed. The caller owns
    /// where it's stored (see <c>SaveStateStore</c>).</summary>
    byte[]? SaveStateBytes();

    /// <summary>Restore machine state from bytes. Returns false if unsupported or it failed.</summary>
    bool LoadStateBytes(byte[] data);

    /// <summary>Diagnostic snapshot of input polling (which devices the core queried). For debugging.</summary>
    string InputDiagnostics => string.Empty;

    /// <summary>The MT-32 LCD text when our synth is driving MIDI; null when it isn't active.</summary>
    string? Mt32Lcd => null;
}
