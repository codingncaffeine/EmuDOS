namespace EmuDOS.Core.Audio;

/// <summary>
/// Watches the raw MIDI byte stream coming out of the core and pulls the MT-32 LCD text from it.
/// Games write the 20-char display via a Roland SysEx to address 0x20 0x00 0x00
/// (<c>F0 41 dd 16 12 20 00 00 &lt;chars…&gt; chk F7</c>). Thread-safe for the core thread.
/// </summary>
public sealed class MidiMonitor
{
    private readonly List<byte> _sysex = new(64);
    private readonly Lock _gate = new();
    private bool _inSysex;
    private long _byteCount;
    private string _lcd = string.Empty;

    public long ByteCount => Interlocked.Read(ref _byteCount);

    public string Lcd
    {
        get { lock (_gate) return _lcd; }
    }

    /// <summary>Feed one MIDI byte (called on the core thread).</summary>
    public void Feed(byte value)
    {
        Interlocked.Increment(ref _byteCount);

        if (value == 0xF0) // SysEx start
        {
            _inSysex = true;
            _sysex.Clear();
            _sysex.Add(value);
            return;
        }

        if (!_inSysex)
            return;

        if (value >= 0xF8)
            return; // system realtime bytes (clock/active-sensing) may interleave a SysEx — ignore

        _sysex.Add(value);
        if (value == 0xF7) // SysEx end
        {
            _inSysex = false;
            ParseSysex();
        }
        else if (_sysex.Count > 4096)
        {
            _inSysex = false; // runaway guard
        }
    }

    private void ParseSysex()
    {
        var s = _sysex;
        // F0 41 <dev> 16 12 20 00 00 <chars...> <checksum> F7  -> MT-32 display
        if (s.Count < 11 || s[1] != 0x41 || s[3] != 0x16 || s[4] != 0x12
            || s[5] != 0x20 || s[6] != 0x00 || s[7] != 0x00)
            return;

        int start = 8;
        int end = s.Count - 2; // drop checksum + F7
        var chars = new char[Math.Max(0, end - start)];
        for (int i = start, j = 0; i < end; i++, j++)
            chars[j] = (char)(s[i] & 0x7F);

        var text = new string(chars).TrimEnd();
        lock (_gate)
            _lcd = text;
    }
}
