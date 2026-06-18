namespace EmuDOS.Core.Engine;

/// <summary>
/// The sink an engine renders into and plays through. Implemented by the presentation layer
/// (e.g. a WPF surface). Keeps the engine free of any UI-framework dependency.
/// </summary>
/// <remarks>
/// Input (keyboard/mouse/pad) is delivered to the engine starting in M1.5, when the libretro
/// input polling is wired; this interface gains an input source at that point.
/// </remarks>
public interface IEngineHost
{
    /// <summary>
    /// A new video frame is ready. The frame's memory is valid only for this call.
    /// </summary>
    void SubmitVideoFrame(in VideoFrame frame);

    /// <summary>
    /// Interleaved stereo 16-bit PCM (L, R, L, R, …) produced since the last call.
    /// </summary>
    void SubmitAudioFrames(ReadOnlySpan<short> interleavedStereo);
}
