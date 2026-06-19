using EmuDOS.Core.Engine;
using EmuDOS.Core.Input;

namespace EmuDOS.Tests.Integration;

/// <summary>
/// A headless <see cref="IEngineHost"/> for smoke-testing the run loop: counts frames and
/// audio samples, reports no input. Lets us prove a session actually drives a live core
/// without any UI.
/// </summary>
internal sealed class StubEngineHost : IEngineHost, IInputSource
{
    private int _frames;
    private long _audioSamples;

    public int Frames => Volatile.Read(ref _frames);

    public long AudioSamples => Interlocked.Read(ref _audioSamples);

    public void SubmitVideoFrame(in VideoFrame frame) => Interlocked.Increment(ref _frames);

    public void SetAudioSampleRate(int sampleRate)
    {
        // headless test host — no audio device
    }

    public void SubmitAudioFrames(ReadOnlySpan<short> interleavedStereo) =>
        Interlocked.Add(ref _audioSamples, interleavedStereo.Length);

    public IInputSource Input => this;

    public bool IsKeyDown(DosKey key) => false;

    public bool TryDequeueKey(out KeyEvent keyEvent)
    {
        keyEvent = default;
        return false;
    }

    public MouseDelta PollMouse() => MouseDelta.None;

    public bool IsButtonDown(int port, PadButton button) => false;
}
