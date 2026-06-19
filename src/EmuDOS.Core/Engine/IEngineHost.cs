using EmuDOS.Core.Input;

namespace EmuDOS.Core.Engine;

/// <summary>
/// The sink an engine renders into and plays through, and the source it reads input from.
/// Implemented by the presentation layer (e.g. a WPF surface). Keeps the engine free of any
/// UI-framework dependency.
/// </summary>
public interface IEngineHost
{
    /// <summary>
    /// A new video frame is ready. The frame's memory is valid only for this call.
    /// </summary>
    void SubmitVideoFrame(in VideoFrame frame);

    /// <summary>
    /// Called once after the game loads with the core's audio sample rate (Hz), so the host
    /// can set up its audio output before frames start arriving.
    /// </summary>
    void SetAudioSampleRate(int sampleRate);

    /// <summary>
    /// Interleaved stereo 16-bit PCM (L, R, L, R, …) produced since the last call.
    /// </summary>
    void SubmitAudioFrames(ReadOnlySpan<short> interleavedStereo);

    /// <summary>Current keyboard/mouse/gamepad state, polled by the engine each frame.</summary>
    IInputSource Input { get; }

    /// <summary>A log line emitted by the emulator core (level, message). Default: ignored.</summary>
    void OnCoreLog(int level, string message) { }
}
