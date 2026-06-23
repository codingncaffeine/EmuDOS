using EmuDOS.Core.Engine;

namespace EmuDOS.Core.Cheats;

/// <summary>Value width a scan/poke operates on (DOS games store values unaligned, so scans step byte-by-byte).</summary>
public enum ScanValueType { Byte, Word, Dword, Float }

/// <summary>How a next-scan compares the current value to the previous snapshot.</summary>
public enum ScanComparison { Exact, Increased, Decreased, Changed, Unchanged, Unknown }

/// <summary>A surviving scan candidate: a guest address and its value at the last scan.</summary>
public readonly record struct ScanResult(ulong Address, double Value);

/// <summary>
/// A Cheat-Engine-style memory scanner over a running <see cref="IDosSession"/>: first/next scan with
/// the usual comparisons, plus typed read/write and a freeze set re-applied each frame by the session.
/// Snapshot-based — each scan copies the core's memory and narrows the candidate set.
/// </summary>
public sealed class CheatEngine(IDosSession session, Action<string>? log = null)
{
    // Unknown-value first scans enumerate every offset, so cap them to small regions (conventional +
    // low memory, where game variables live) to keep the candidate set sane.
    private const long UnknownScanRegionCap = 8 * 1024 * 1024;

    private readonly IDosSession _session = session;
    private readonly Action<string>? _log = log;
    private IReadOnlyList<(MemoryRegionInfo Region, byte[] Data)> _last = [];
    private List<ulong> _candidates = [];
    private readonly Dictionary<ulong, byte[]> _frozen = new();

    /// <summary>Number of surviving candidates after the last scan.</summary>
    public int ResultCount => _candidates.Count;

    public static int SizeOf(ScanValueType t) => t == ScanValueType.Byte ? 1 : t == ScanValueType.Word ? 2 : 4;

    /// <summary>First scan: EXACT (needs a value) collects every matching address; UNKNOWN seeds every
    /// address in the small regions as a candidate to narrow later. Returns the candidate count.</summary>
    public int FirstScan(ScanValueType type, ScanComparison comparison, double? value,
                         ulong? rangeStart = null, ulong? rangeEnd = null)
    {
        var snap = Snap();
        int size = SizeOf(type);
        var found = new List<ulong>();

        foreach (var (region, data) in snap)
        {
            // Clip the region to the optional [rangeStart, rangeEnd] guest-address window.
            ulong regionStart = region.GuestStart;
            ulong regionEnd = regionStart + (ulong)data.Length;
            ulong lo = rangeStart is { } rs && rs > regionStart ? rs : regionStart;
            ulong hi = rangeEnd is { } re && re < regionEnd ? re : regionEnd;
            if (hi <= lo)
                continue;
            int startOff = (int)(lo - regionStart);
            int endOff = (int)(hi - regionStart);
            bool unknownOk = comparison == ScanComparison.Unknown
                          && (ulong)(endOff - startOff) <= UnknownScanRegionCap;
            for (int off = startOff; off + size <= endOff; off++)
            {
                if (comparison == ScanComparison.Exact && value is { } v)
                {
                    if (ReadValue(data, off, type) == v)
                        found.Add(regionStart + (ulong)off);
                }
                else if (unknownOk)
                {
                    found.Add(regionStart + (ulong)off);
                }
            }
        }

        _last = snap;
        _candidates = found;
        _log?.Invoke($"FirstScan type={type} cmp={comparison} value={value?.ToString() ?? "(none)"} size={size} range=[{(rangeStart?.ToString("X") ?? "all")}..{(rangeEnd?.ToString("X") ?? "all")}] regions={snap.Count} -> {found.Count} candidates");
        return _candidates.Count;
    }

    /// <summary>Next scan: keep only candidates matching the comparison against the previous snapshot.</summary>
    public int NextScan(ScanValueType type, ScanComparison comparison, double? value)
    {
        var snap = Snap();
        int size = SizeOf(type);
        var kept = new List<ulong>(_candidates.Count);

        foreach (var addr in _candidates)
        {
            if (!TryRead(snap, addr, type, out double cur) || !TryRead(_last, addr, type, out double prev))
                continue;

            bool ok = comparison switch
            {
                ScanComparison.Exact => value is { } v && cur == v,
                ScanComparison.Increased => cur > prev,
                ScanComparison.Decreased => cur < prev,
                ScanComparison.Changed => cur != prev,
                ScanComparison.Unchanged => cur == prev,
                _ => true,
            };
            if (ok)
                kept.Add(addr);
        }

        _last = snap;
        _candidates = kept;
        _log?.Invoke($"NextScan type={type} cmp={comparison} value={value?.ToString() ?? "(none)"} -> {kept.Count} candidates");
        return _candidates.Count;
    }

    public void ResetScan()
    {
        _candidates = [];
        _last = [];
    }

    /// <summary>Up to <paramref name="max"/> current results with freshly-read values, for display.</summary>
    public IReadOnlyList<ScanResult> Results(ScanValueType type, int max = 1000)
    {
        var list = new List<ScanResult>(Math.Min(max, _candidates.Count));
        foreach (var addr in _candidates)
        {
            if (list.Count >= max)
                break;
            // Use the value captured during the last scan (in-memory) — NOT a per-candidate live read,
            // which would be hundreds of cross-thread round-trips and freeze the UI.
            if (TryRead(_last, addr, type, out double v))
                list.Add(new ScanResult(addr, v));
        }
        return list;
    }

    /// <summary>Read the live value at an address (via the session), or null if out of range.</summary>
    public double? ReadLive(ulong address, ScanValueType type)
    {
        var bytes = _session.ReadMemory(address, SizeOf(type));
        return bytes is null ? null : ReadValue(bytes, 0, type);
    }

    /// <summary>Write a value at an address. Returns false if out of range / unsupported.</summary>
    public bool Write(ulong address, ScanValueType type, double value) =>
        _session.WriteMemory(address, Encode(type, value));

    /// <summary>Freeze (or unfreeze) an address at a value; the session re-applies the set every frame.</summary>
    public void SetFreeze(ulong address, ScanValueType type, double value, bool freeze)
    {
        if (freeze)
            _frozen[address] = Encode(type, value);
        else
            _frozen.Remove(address);
        // Hand the session a fresh snapshot of the set (it swaps the reference atomically).
        _session.SetFrozen(_frozen.Count == 0 ? null : new Dictionary<ulong, byte[]>(_frozen));
    }

    public bool IsFrozen(ulong address) => _frozen.ContainsKey(address);

    /// <summary>Release every frozen value (e.g. when the cheat window closes).</summary>
    public void ClearAllFreezes()
    {
        _frozen.Clear();
        _session.SetFrozen(null);
    }

    // ── helpers ──

    private IReadOnlyList<(MemoryRegionInfo, byte[])> Snap()
    {
        var raw = _session.SnapshotMemory();
        long total = 0;
        foreach (var (_, data) in raw)
            total += data.Length;
        _log?.Invoke($"snapshot: session returned {raw.Count} region(s), {total} bytes; MemoryRegions.Count={_session.MemoryRegions.Count}");
        var list = new List<(MemoryRegionInfo, byte[])>(raw.Count);
        foreach (var (region, data) in raw)
            list.Add((new MemoryRegionInfo(region.GuestStart, region.Length), data));
        return list;
    }

    private bool TryRead(IReadOnlyList<(MemoryRegionInfo Region, byte[] Data)> snap, ulong addr,
                         ScanValueType type, out double value)
    {
        int size = SizeOf(type);
        foreach (var (region, data) in snap)
        {
            if (addr < region.GuestStart || addr + (ulong)size > region.GuestStart + (ulong)region.Length)
                continue;
            value = ReadValue(data, (int)(addr - region.GuestStart), type);
            return true;
        }
        value = 0;
        return false;
    }

    private static double ReadValue(byte[] data, int offset, ScanValueType type) => type switch
    {
        ScanValueType.Byte => data[offset],
        ScanValueType.Word => BitConverter.ToUInt16(data, offset),
        ScanValueType.Dword => BitConverter.ToUInt32(data, offset),
        ScanValueType.Float => BitConverter.ToSingle(data, offset),
        _ => 0,
    };

    private static byte[] Encode(ScanValueType type, double value) => type switch
    {
        ScanValueType.Byte => [(byte)(long)value],
        ScanValueType.Word => BitConverter.GetBytes((ushort)(long)value),
        ScanValueType.Dword => BitConverter.GetBytes((uint)(long)value),
        ScanValueType.Float => BitConverter.GetBytes((float)value),
        _ => [],
    };
}

/// <summary>Minimal region descriptor used by the scanner (decoupled from the libretro struct).</summary>
public readonly record struct MemoryRegionInfo(ulong GuestStart, long Length);
