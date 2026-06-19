using System.Runtime.InteropServices;

namespace EmuDOS.Core.Audio;

/// <summary>
/// Our own Roland MT-32, via the native munt shim (emudos_mt32.dll). Fed the game's raw MIDI
/// stream (routed out of the core), it synthesizes the music AND exposes the 20-char LCD text —
/// the authentic Boxer-style experience. Single-threaded: feed and render on the engine thread.
/// </summary>
public sealed partial class Mt32Synth : IDisposable
{
    [LibraryImport("emudos_mt32")]
    private static partial nint mt32_create(byte[] control, int controlLen, byte[] pcm, int pcmLen);

    [LibraryImport("emudos_mt32")]
    private static partial int mt32_sample_rate(nint handle);

    [LibraryImport("emudos_mt32")]
    private static partial void mt32_play_msg(nint handle, uint msg);

    [LibraryImport("emudos_mt32")]
    private static partial void mt32_play_sysex(nint handle, byte[] data, int len);

    [LibraryImport("emudos_mt32")]
    private static partial void mt32_render(nint handle, short[] outBuffer, int frames);

    [LibraryImport("emudos_mt32")]
    private static partial void mt32_free(nint handle);

    /// <summary>
    /// Let the shim be loaded from <paramref name="searchDir"/> (where the Downloads tab installs
    /// it) before falling back to the default search (next to the exe, for dev). Call once at startup.
    /// </summary>
    public static void RegisterNativeResolver(string searchDir)
    {
        NativeLibrary.SetDllImportResolver(typeof(Mt32Synth).Assembly, (name, _, _) =>
        {
            if (name == "emudos_mt32")
            {
                var candidate = Path.Combine(searchDir, "emudos_mt32.dll");
                if (File.Exists(candidate) && NativeLibrary.TryLoad(candidate, out var handle))
                    return handle;
            }
            return nint.Zero; // fall back to the default resolver
        });
    }

    private nint _handle;

    private byte _status;
    private readonly byte[] _data = new byte[2];
    private int _dataLen, _dataNeed;
    private readonly List<byte> _sysex = new(64);
    private bool _inSysex;

    private readonly Lock _lcdGate = new();
    private string _lcd = string.Empty;
    private long _bytesFed;
    private int _sysexCount;
    private string _lastRolandHeader = string.Empty;

    public long BytesFed => _bytesFed;

    /// <summary>Diagnostic: how many SysEx messages seen + the last Roland address bytes.</summary>
    public string SysexInfo
    {
        get { lock (_lcdGate) return $"sysex={_sysexCount} rolandHdr=[{_lastRolandHeader}]"; }
    }

    private Mt32Synth(nint handle)
    {
        _handle = handle;
        SampleRate = mt32_sample_rate(handle);
    }

    public int SampleRate { get; }

    public string Lcd
    {
        get { lock (_lcdGate) return _lcd; }
    }

    /// <summary>Open an MT-32 from a control/PCM ROM pair. Null if the ROMs or the shim are missing.</summary>
    public static Mt32Synth? TryCreate(string controlRomPath, string pcmRomPath)
    {
        if (!File.Exists(controlRomPath) || !File.Exists(pcmRomPath))
            return null;
        try
        {
            var control = File.ReadAllBytes(controlRomPath);
            var pcm = File.ReadAllBytes(pcmRomPath);
            var handle = mt32_create(control, control.Length, pcm, pcm.Length);
            return handle == 0 ? null : new Mt32Synth(handle);
        }
        catch
        {
            return null; // shim DLL absent or load failure
        }
    }

    /// <summary>Feed one MIDI byte from the core's output stream.</summary>
    public void FeedByte(byte b)
    {
        _bytesFed++;
        if (b == 0xF0) // SysEx start
        {
            _inSysex = true;
            _sysex.Clear();
            _sysex.Add(b);
            return;
        }

        if (_inSysex)
        {
            if (b >= 0xF8)
                return; // system realtime may interleave a SysEx
            _sysex.Add(b);
            if (b == 0xF7)
            {
                _inSysex = false;
                FlushSysex();
            }
            else if (_sysex.Count > 8192)
            {
                _inSysex = false;
            }
            return;
        }

        if (b >= 0xF8)
            return; // system realtime

        if (b >= 0x80)
        {
            if (b >= 0xF0) { _status = 0; return; } // system common — not forwarded
            _status = b;
            _dataLen = 0;
            _dataNeed = NeedsTwoData(b) ? 2 : 1;
            return;
        }

        if (_status == 0)
            return; // data byte with no running status

        _data[_dataLen++] = b;
        if (_dataLen >= _dataNeed)
        {
            uint packed = _status | ((uint)_data[0] << 8) | (_dataNeed > 1 ? (uint)_data[1] << 16 : 0u);
            mt32_play_msg(_handle, packed);
            _dataLen = 0; // keep status for running-status messages
        }
    }

    /// <summary>Render <paramref name="frames"/> interleaved stereo samples (buffer length >= frames*2).</summary>
    public void Render(short[] buffer, int frames)
    {
        if (_handle != 0)
            mt32_render(_handle, buffer, frames);
    }

    private void FlushSysex()
    {
        var bytes = _sysex.ToArray();
        mt32_play_sysex(_handle, bytes, bytes.Length);

        if (bytes.Length >= 8 && bytes[1] == 0x41) // a Roland message — capture its address for diagnostics
        {
            lock (_lcdGate)
            {
                _sysexCount++;
                _lastRolandHeader = $"{bytes[3]:X2} {bytes[4]:X2} {bytes[5]:X2} {bytes[6]:X2} {bytes[7]:X2}";
            }
        }

        ParseLcd(bytes);
    }

    // F0 41 dd 16 12 20 00 00 <20 chars> chk F7 — the MT-32 LCD display message.
    private void ParseLcd(byte[] s)
    {
        if (s.Length < 11 || s[1] != 0x41 || s[3] != 0x16 || s[4] != 0x12
            || s[5] != 0x20 || s[6] != 0x00 || s[7] != 0x00)
            return;

        int start = 8, end = s.Length - 2; // drop checksum + F7
        var chars = new char[Math.Max(0, end - start)];
        for (int i = start, j = 0; i < end; i++, j++)
            chars[j] = (char)(s[i] & 0x7F);

        var text = new string(chars).TrimEnd();
        lock (_lcdGate)
            _lcd = text;
    }

    private static bool NeedsTwoData(byte status)
    {
        byte hi = (byte)(status & 0xF0);
        return hi != 0xC0 && hi != 0xD0; // program change / channel pressure carry one data byte
    }

    public void Dispose()
    {
        if (_handle != 0)
        {
            mt32_free(_handle);
            _handle = 0;
        }
    }
}
