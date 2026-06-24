using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LibVLCSharp.Shared;

namespace EmuDOS.Services;

/// <summary>
/// Plays a looping ScreenScraper video snap (.mp4) into a <see cref="WriteableBitmap"/> via LibVLC's
/// software video callback — no native VideoView, so it renders anywhere in WPF (e.g. a hover popup).
/// One instance reuses its buffer/bitmap and MediaPlayer across games (Play swaps the media).
/// </summary>
public sealed class SnapPlayer : IDisposable
{
    private const int W = 320, H = 240, Stride = W * 4; // ScreenScraper snaps are 4:3 320x240

    private readonly Dispatcher _ui;
    private readonly IntPtr _buffer;
    private LibVLCSharp.Shared.MediaPlayer? _player;
    private Action? _onFirstFrame;
    private bool _first;
    private volatile bool _disposed;

    public WriteableBitmap Bitmap { get; }

    public SnapPlayer(Dispatcher ui)
    {
        _ui = ui;
        _buffer = Marshal.AllocHGlobal(Stride * H);
        Bitmap = new WriteableBitmap(W, H, 96, 96, PixelFormats.Bgr32, null);
    }

    /// <summary>Start (or restart with a new file) the looping snap. <paramref name="onFirstFrame"/>
    /// fires once when the first frame is drawn (use it to reveal the popup).</summary>
    public async void Play(string mp4Path, Action? onFirstFrame)
    {
        var libVLC = await VideoPlaybackService.Instance.GetLibVLCAsync();
        if (libVLC is null || _disposed)
            return;

        await Task.Run(() =>
        {
            try
            {
                _player ??= CreatePlayer(libVLC);
                _onFirstFrame = onFirstFrame;
                _first = true;
                _player.Stop();
                using var media = new Media(libVLC, mp4Path, FromType.FromPath);
                media.AddOption(":input-repeat=65535"); // loop forever
                _player.Play(media);
            }
            catch { /* a hover preview is best-effort */ }
        });
    }

    private LibVLCSharp.Shared.MediaPlayer CreatePlayer(LibVLC libVLC)
    {
        var player = new LibVLCSharp.Shared.MediaPlayer(libVLC);
        player.SetVideoFormat("RV32", W, H, Stride);
        player.SetVideoCallbacks(
            (IntPtr opaque, IntPtr planes) => { Marshal.WriteIntPtr(planes, _buffer); return IntPtr.Zero; },
            null,
            (IntPtr opaque, IntPtr picture) => _ui.BeginInvoke(() =>
            {
                if (_disposed)
                    return;
                Bitmap.WritePixels(new Int32Rect(0, 0, W, H), _buffer, Stride * H, Stride);
                if (_first)
                {
                    _first = false;
                    _onFirstFrame?.Invoke();
                }
            }));
        return player;
    }

    public void Stop()
    {
        try { _player?.Stop(); } catch { }
    }

    public void Dispose()
    {
        _disposed = true;
        try { _player?.Stop(); _player?.Dispose(); } catch { }
        _player = null;
        if (_buffer != IntPtr.Zero)
            Marshal.FreeHGlobal(_buffer);
    }
}
