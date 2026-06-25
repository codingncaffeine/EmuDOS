using EmuDOS.Core.Input;
using EmuDOS.Core.Libretro;
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

    /// <summary>Total presented frames so far (incl. duplicates) — over wall-clock, the output FPS.</summary>
    long FramesPresented => 0;

    // ── Cheat engine: live memory access (marshalled onto the emulation thread by the implementation). ──

    /// <summary>Memory regions the core exposed (guest address + length), or empty if none/unsupported.</summary>
    IReadOnlyList<MemoryRegion> MemoryRegions => Array.Empty<MemoryRegion>();

    /// <summary>Copy every memory region's bytes — the basis for a scan.</summary>
    IReadOnlyList<(MemoryRegion Region, byte[] Data)> SnapshotMemory() =>
        Array.Empty<(MemoryRegion, byte[])>();

    /// <summary>Read <paramref name="count"/> bytes at a guest address, or null if out of range.</summary>
    byte[]? ReadMemory(ulong address, int count) => null;

    /// <summary>Write bytes at a guest address. Returns false if unsupported or out of range.</summary>
    bool WriteMemory(ulong address, byte[] data) => false;

    /// <summary>Set the frozen values re-applied every frame (pass a fresh dictionary; null clears).</summary>
    void SetFrozen(IReadOnlyDictionary<ulong, byte[]>? frozen) { }

    /// <summary>Load this save state once the game has booted (used to launch straight into a state).</summary>
    void SetInitialState(byte[] state) { }

    /// <summary>Set the run-speed multiplier (1.0 = normal; &gt;1 fast-forward, &lt;1 slow-motion).</summary>
    void SetSpeed(double multiplier) { }

    /// <summary>Hold to rewind through recently captured states.</summary>
    void SetRewinding(bool on) { }
}
