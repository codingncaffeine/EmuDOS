using System;
using System.Threading.Tasks;
using LibVLCSharp.Shared;

namespace EmuDOS.Services;

/// <summary>
/// Owns the process-wide <see cref="LibVLC"/> used for game-card video snaps. First construction is
/// multi-second (native libvlc load + plugin scan), so it warms up off the UI thread; later callers
/// await an already-completed task. Returns null if LibVLC can't initialize (snaps just won't play).
/// </summary>
internal sealed class VideoPlaybackService
{
    public static VideoPlaybackService Instance { get; } = new();

    private Task<LibVLC?>? _warmup;
    private readonly object _gate = new();

    private VideoPlaybackService() { }

    public void StartWarmup() => _ = GetLibVLCAsync();

    public Task<LibVLC?> GetLibVLCAsync()
    {
        if (_warmup != null)
            return _warmup;
        lock (_gate)
            _warmup ??= Task.Run(Create);
        return _warmup;
    }

    private static LibVLC? Create()
    {
        try
        {
            LibVLCSharp.Shared.Core.Initialize(); // locate the bundled native libvlc
            return new LibVLC("--no-audio", "--no-osd", "--no-snapshot-preview");
        }
        catch
        {
            return null; // no video snaps; the card falls back to the static placeholder
        }
    }
}
