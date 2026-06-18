namespace EmuDOS.Core.Engine;

/// <summary>Pixel layout of a <see cref="VideoFrame"/>.</summary>
public enum PixelFormat
{
    /// <summary>16-bit 5-6-5.</summary>
    Rgb565,

    /// <summary>32-bit, one byte unused (X8R8G8B8).</summary>
    Xrgb8888,
}

/// <summary>
/// A single rendered frame handed from the engine to the host. Wraps native memory that is
/// only valid for the duration of the <see cref="IEngineHost.SubmitVideoFrame"/> call —
/// copy out anything you need to keep.
/// </summary>
public readonly ref struct VideoFrame(nint data, int width, int height, int pitch, PixelFormat format)
{
    /// <summary>Pointer to the top-left pixel.</summary>
    public nint Data { get; } = data;

    public int Width { get; } = width;

    public int Height { get; } = height;

    /// <summary>Bytes per row (may exceed Width × bytes-per-pixel due to padding).</summary>
    public int Pitch { get; } = pitch;

    public PixelFormat Format { get; } = format;
}
