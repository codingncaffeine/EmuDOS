using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using EmuDOS.Core.Cheats;
using EmuDOS.Core.Engine;

namespace EmuDOS;

/// <summary>
/// The cheat engine window — a System-Shock-style CRT console. Scans the running game's memory,
/// edits values, and freezes them. Opens over a live game (with an <see cref="IDosSession"/>); the
/// parameterless ctor is a non-functional preview (e.g. from the shelf).
/// </summary>
public partial class CheatWindow : Window
{
    private readonly CheatEngine? _engine;
    private ScanValueType _type = ScanValueType.Dword;
    private ScanComparison _comparison = ScanComparison.Exact;
    private ulong? _selected;
    private readonly ObservableCollection<CheatRow> _rows = new();
    private readonly DispatcherTimer _timer;

    public CheatWindow() : this(null) { }

    private readonly EmuDOS.Core.Infrastructure.AppLog _log;

    public CheatWindow(IDosSession? session)
    {
        InitializeComponent();
        _log = new EmuDOS.Core.Infrastructure.AppLog(((App)Application.Current).Services.Paths, "cheats.log");
        _log.Info($"CheatWindow opened: session={(session is null ? "null (preview)" : "live")}"
            + (session is not null ? $", MemoryRegions={session.MemoryRegions.Count}" : ""));
        if (session is not null)
            _engine = new CheatEngine(session, _log.Info);

        ResultsList.DisplayMemberPath = nameof(ResultView.Display);
        CheatTable.ItemsSource = _rows;
        HighlightSelector(TypeSelector, _type.ToString());
        HighlightSelector(ScanSelector, _comparison.ToString());

        if (_engine is null)
            StatusText.Text = "⌁ PREVIEW — LAUNCH A GAME & PRESS F11 TO USE";

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _timer.Tick += (_, _) => RefreshLive();
        _timer.Start();
    }

    private void OnPickType(object sender, MouseButtonEventArgs e)
    {
        if (sender is TextBlock { Tag: string tag } && Enum.TryParse<ScanValueType>(tag, out var t))
        {
            _type = t;
            HighlightSelector(TypeSelector, tag);
        }
    }

    private void OnPickScan(object sender, MouseButtonEventArgs e)
    {
        if (sender is TextBlock { Tag: string tag } && Enum.TryParse<ScanComparison>(tag, out var c))
        {
            _comparison = c;
            HighlightSelector(ScanSelector, tag);
        }
    }

    private static void HighlightSelector(Panel panel, string selectedTag)
    {
        var on = (Brush)panel.FindResource("NeonGreen");
        var off = (Brush)panel.FindResource("Dim");
        foreach (var child in panel.Children)
            if (child is TextBlock tb && tb.Tag is string)
                tb.Foreground = string.Equals((string)tb.Tag, selectedTag, StringComparison.Ordinal) ? on : off;
    }

    private async void OnStartScan(object sender, RoutedEventArgs e)
    {
        if (_engine is null) { Beep("PREVIEW — OPEN IN A GAME WITH F11"); return; }
        double? value = ParseValue(ValueBox.Text, _type);
        if (_comparison == ScanComparison.Exact && value is null) { Beep("ENTER A VALUE"); return; }
        _log.Info($"START SCAN: typedText='{ValueBox.Text}' type={_type} cmp={_comparison} parsed={value?.ToString() ?? "null"}");
        Beep("SCANNING…");
        var (engine, type, cmp) = (_engine, _type, _comparison);
        int n = await Task.Run(() => engine.FirstScan(type, cmp, value)); // off the UI thread
        AfterScan(n);
    }

    private async void OnNextScan(object sender, RoutedEventArgs e)
    {
        if (_engine is null) { Beep("PREVIEW — OPEN IN A GAME WITH F11"); return; }
        double? value = ParseValue(ValueBox.Text, _type);
        Beep("SCANNING…");
        var (engine, type, cmp) = (_engine, _type, _comparison);
        int n = await Task.Run(() => engine.NextScan(type, cmp, value));
        AfterScan(n);
    }

    private void OnUndoScan(object sender, RoutedEventArgs e)
    {
        _engine?.ResetScan();
        ResultsList.ItemsSource = null;
        ResultsHeader.Text = "RESULTS";
    }

    private void AfterScan(int count)
    {
        ResultsHeader.Text = count > 800 ? $"RESULTS ({count}) — NARROW FURTHER" : $"RESULTS ({count})";
        Beep(count == 0 ? "NO MATCHES — TRY BYTE/WORD OR A DIFFERENT VALUE" : $"{count} MATCH{(count == 1 ? "" : "ES")}");
        // Show a bounded slice; huge candidate sets (e.g. an UNKNOWN first scan) stay internal until narrowed.
        ResultsList.ItemsSource = count is > 0 and <= 800 && _engine is not null
            ? _engine.Results(_type, 800).Select(r => new ResultView(r.Address, $"{FormatAddr(r.Address)} | {FormatValue(r.Value, _type)}")).ToList()
            : null;
    }

    private void OnResultSelected(object sender, SelectionChangedEventArgs e)
    {
        if (ResultsList.SelectedItem is ResultView rv)
        {
            _selected = rv.Address;
            SelAddrText.Text = FormatAddr(rv.Address);
            UpdateCurrent();
        }
    }

    private void OnWriteValue(object sender, RoutedEventArgs e)
    {
        if (_engine is null || _selected is not { } addr) { Beep("SELECT AN ADDRESS"); return; }
        if (ParseValue(EditValueBox.Text, _type) is not { } v) { Beep("ENTER A VALUE"); return; }
        _engine.Write(addr, _type, v);
        UpdateCurrent();
    }

    private void OnAddToTable(object sender, RoutedEventArgs e)
    {
        if (_selected is not { } addr) { Beep("SELECT AN ADDRESS"); return; }
        var cur = _engine?.ReadLive(addr, _type) ?? 0;
        _rows.Add(new CheatRow
        {
            Address = FormatAddr(addr),
            AddressValue = addr,
            Type = _type,
            CurrentValue = FormatValue(cur, _type),
            NewValue = string.IsNullOrWhiteSpace(EditValueBox.Text) ? ((long)cur).ToString(CultureInfo.InvariantCulture) : EditValueBox.Text.Trim(),
            Description = string.IsNullOrWhiteSpace(EditDescBox.Text) ? "—" : EditDescBox.Text.Trim(),
        });
    }

    private void OnFreezeChanged(object sender, RoutedEventArgs e)
    {
        if (_engine is null || sender is not CheckBox { DataContext: CheatRow row })
            return;
        var target = ParseValue(row.NewValue, row.Type) ?? _engine.ReadLive(row.AddressValue, row.Type) ?? 0;
        _engine.SetFreeze(row.AddressValue, row.Type, target, row.Freeze);
    }

    private void RefreshLive()
    {
        if (_engine is null)
            return;
        UpdateCurrent();
        foreach (var row in _rows)
            if (_engine.ReadLive(row.AddressValue, row.Type) is { } v)
                row.CurrentValue = FormatValue(v, row.Type);
    }

    private void UpdateCurrent()
    {
        if (_engine is not null && _selected is { } addr && _engine.ReadLive(addr, _type) is { } v)
            CurValText.Text = FormatValue(v, _type);
    }

    private void Beep(string msg) => StatusText.Text = "⌁ " + msg;

    // ── formatting / parsing ──

    private static string FormatAddr(ulong a)
    {
        var s = a.ToString("X8", CultureInfo.InvariantCulture);
        return s[..4] + ":" + s[4..];
    }

    private static string FormatValue(double v, ScanValueType type)
    {
        if (type == ScanValueType.Float)
            return v.ToString("0.###", CultureInfo.InvariantCulture);
        long l = (long)v;
        int width = CheatEngine.SizeOf(type) * 2;
        return $"{l.ToString("X" + width, CultureInfo.InvariantCulture)} ({l})";
    }

    private static double? ParseValue(string text, ScanValueType type)
    {
        text = (text ?? string.Empty).Trim();
        if (text.Length == 0)
            return null;
        if (type == ScanValueType.Float)
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;
        bool hex = text.StartsWith("0x", StringComparison.OrdinalIgnoreCase);
        return long.TryParse(hex ? text[2..] : text,
            hex ? NumberStyles.HexNumber : NumberStyles.Integer, CultureInfo.InvariantCulture, out var l) ? l : null;
    }

    private void OnTitleDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void OnClose(object sender, MouseButtonEventArgs e) => Close();

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
            Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        _timer.Stop();
        _engine?.ClearAllFreezes(); // release any frozen values so they don't stick after the window closes
        base.OnClosed(e);
    }

    private sealed record ResultView(ulong Address, string Display);
}

/// <summary>One row in the cheat table (live current value + a freeze toggle).</summary>
public sealed class CheatRow : INotifyPropertyChanged
{
    private string _current = "—";
    private bool _freeze;

    public string Address { get; init; } = "";
    public ulong AddressValue { get; init; }
    public ScanValueType Type { get; init; }
    public string NewValue { get; set; } = "";
    public string Description { get; init; } = "—";

    public string CurrentValue
    {
        get => _current;
        set { if (_current != value) { _current = value; Raise(nameof(CurrentValue)); } }
    }

    public bool Freeze
    {
        get => _freeze;
        set { if (_freeze != value) { _freeze = value; Raise(nameof(Freeze)); } }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Raise(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
