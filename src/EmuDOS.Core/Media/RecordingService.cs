using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;

namespace EmuDOS.Core.Media;

/// <summary>
/// Records gameplay to MP4 via ffmpeg. Raw BGRX video frames and S16LE stereo audio are streamed to
/// temp files on background threads (so the emulator thread never blocks on disk), then ffmpeg encodes
/// them at the *actual* captured frame rate (frames ÷ duration) — so playback speed is right even when
/// frames were dropped. Adapted from Emutastic's RecordingService. One recording at a time.
/// </summary>
public sealed class RecordingService(string ffmpegPath)
{
    private readonly object _lock = new();

    public bool IsRecording { get; private set; }

    private int _width, _height, _sampleRate;
    private int _outW, _outH; // encoded output size — the displayed (aspect-corrected) picture
    private string _outputPath = string.Empty, _videoRaw = string.Empty, _audioRaw = string.Empty;
    private string _quality = "Medium";
    private FileStream? _videoFile, _audioFile;
    private BlockingCollection<byte[]>? _videoQueue, _audioQueue;
    private Thread? _videoThread, _audioThread;
    private long _framesWritten;
    private DateTime _startTime;

    /// <summary>Begin recording. Frames are captured at <paramref name="width"/>×<paramref name="height"/>
    /// (the core's native size); the video is encoded at <paramref name="displayWidth"/>×
    /// <paramref name="displayHeight"/> — the size/shape the player actually sees — when given
    /// (0 = keep native). Returns null on success, or an error message.</summary>
    public string? Start(string outputPath, int width, int height, int sampleRate, string quality,
                         int displayWidth = 0, int displayHeight = 0)
    {
        lock (_lock)
        {
            if (IsRecording)
                return "Already recording.";
            if (!File.Exists(ffmpegPath))
                return "FFmpeg isn't installed — get it from the Downloads tab.";
            try
            {
                _outputPath = outputPath;
                _width = width;
                _height = height;
                _outW = Even(displayWidth > 0 ? displayWidth : width);
                _outH = Even(displayHeight > 0 ? displayHeight : height);
                _sampleRate = sampleRate > 0 ? sampleRate : 48000;
                _quality = quality;
                _videoRaw = outputPath + ".video.raw";
                _audioRaw = outputPath + ".audio.raw";
                Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

                _videoFile = new FileStream(_videoRaw, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 22, FileOptions.SequentialScan);
                _audioFile = new FileStream(_audioRaw, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16);
                _videoQueue = new BlockingCollection<byte[]>(boundedCapacity: 120);
                _audioQueue = new BlockingCollection<byte[]>(boundedCapacity: 500);
                _videoThread = new Thread(() => Drain(_videoQueue, _videoFile)) { IsBackground = true, Name = "RecVideo" };
                _audioThread = new Thread(() => Drain(_audioQueue, _audioFile)) { IsBackground = true, Name = "RecAudio" };
                _videoThread.Start();
                _audioThread.Start();

                _framesWritten = 0;
                _startTime = DateTime.Now;
                IsRecording = true;
                return null;
            }
            catch (Exception ex)
            {
                Cleanup();
                return ex.Message;
            }
        }
    }

    /// <summary>Queue a BGRX frame (the bytes are copied; dropped if the writer falls behind).</summary>
    public void WriteVideoFrame(byte[] frame, int length)
    {
        var queue = _videoQueue;
        if (!IsRecording || queue is null)
            return;
        var copy = new byte[length];
        Array.Copy(frame, copy, length);
        if (queue.TryAdd(copy))
            Interlocked.Increment(ref _framesWritten);
    }

    /// <summary>Queue S16LE stereo PCM (the bytes are copied).</summary>
    public void WriteAudio(byte[] pcm, int length)
    {
        var queue = _audioQueue;
        if (!IsRecording || queue is null || length <= 0)
            return;
        var copy = new byte[length];
        Array.Copy(pcm, copy, length);
        queue.TryAdd(copy);
    }

    private static void Drain(BlockingCollection<byte[]> queue, FileStream file)
    {
        foreach (var chunk in queue.GetConsumingEnumerable())
            file.Write(chunk, 0, chunk.Length);
    }

    /// <summary>Stop and encode to MP4 (blocking — call from a background thread). Returns the output
    /// path on success, or null.</summary>
    public string? Stop()
    {
        lock (_lock)
        {
            if (!IsRecording)
                return null;
            IsRecording = false;
            var elapsed = DateTime.Now - _startTime;

            _videoQueue?.CompleteAdding();
            _audioQueue?.CompleteAdding();
            _videoThread?.Join(5000);
            _audioThread?.Join(5000);
            try { _videoFile?.Flush(); _videoFile?.Dispose(); } catch { }
            try { _audioFile?.Flush(); _audioFile?.Dispose(); } catch { }
            _videoFile = null;
            _audioFile = null;

            int fps = Math.Max(1, (int)Math.Round(_framesWritten / Math.Max(elapsed.TotalSeconds, 0.1)));
            bool ok = Encode(fps);
            try { File.Delete(_videoRaw); } catch { }
            try { File.Delete(_audioRaw); } catch { }
            return ok ? _outputPath : null;
        }
    }

    private bool Encode(int fps)
    {
        try
        {
            bool hasAudio = File.Exists(_audioRaw) && new FileInfo(_audioRaw).Length > 0;
            // Quality → libx264 CRF/preset (software encode keeps it dependency-free and universal).
            string crf = _quality switch { "Low" => "28", "High" => "18", _ => "23" };
            string preset = _quality switch { "High" => "slow", "Low" => "veryfast", _ => "medium" };

            var args = $"-y -f rawvideo -pix_fmt bgr0 -s {_width}x{_height} -r {fps} -i \"{_videoRaw}\" ";
            if (hasAudio)
                args += $"-f s16le -ar {_sampleRate} -ac 2 -i \"{_audioRaw}\" ";
            // Scale to the displayed (aspect-corrected) size with NEAREST-NEIGHBOR so pixels stay sharp
            // (no smoothing) — matching the crisp on-screen image. yuv420p for universal playback
            // (needs even dimensions, which _outW/_outH already are).
            args += $"-c:v libx264 -preset {preset} -crf {crf} -pix_fmt yuv420p -vf \"scale={_outW}:{_outH}:flags=neighbor\" ";
            if (hasAudio)
                args += "-c:a aac -b:a 192k ";
            args += $"-movflags +faststart \"{_outputPath}\"";

            using var proc = Process.Start(new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
            })!;
            proc.StandardError.ReadToEnd();
            proc.WaitForExit();
            return proc.ExitCode == 0 && File.Exists(_outputPath);
        }
        catch
        {
            return false;
        }
    }

    private static int Even(double value) => Math.Max(2, (int)Math.Round(value) & ~1);

    private void Cleanup()
    {
        try { _videoQueue?.CompleteAdding(); _audioQueue?.CompleteAdding(); } catch { }
        try { _videoFile?.Dispose(); _audioFile?.Dispose(); } catch { }
        _videoFile = null;
        _audioFile = null;
        IsRecording = false;
    }
}
