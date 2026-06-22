using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace EmuDOS.Services;

/// <summary>
/// Background watchdog that detects UI-thread stalls and logs each freeze (duration + the window that
/// was active when it began) to <c>ui_freezes.log</c>. A background thread posts an Input-priority
/// ping to the dispatcher each cycle; if the ping doesn't complete within the threshold, the UI is
/// frozen — it waits for the ping to drain, then logs the total duration. Ported from Emutastic.
/// </summary>
internal sealed class UiFreezeWatchdog
{
    private const int FreezeThresholdMs = 600;
    private const int PollIntervalMs = 100;

    public static UiFreezeWatchdog Instance { get; } = new();

    private Dispatcher? _dispatcher;
    private Thread? _thread;
    private string? _logPath;
    private volatile bool _stopRequested;
    private volatile string _currentWindowTitle = "(none)";
    private long _lastPingCompletedMs;
    private readonly Stopwatch _sw = Stopwatch.StartNew();

    private UiFreezeWatchdog() { }

    public void Start(Dispatcher dispatcher)
    {
        if (_thread != null)
            return;
        _dispatcher = dispatcher;
        try
        {
            Directory.CreateDirectory(CrashLog.LogDir);
            _logPath = Path.Combine(CrashLog.LogDir, "ui_freezes.log");
            File.AppendAllText(_logPath,
                $"=== Watchdog start {DateTime.Now:yyyy-MM-dd HH:mm:ss} (threshold {FreezeThresholdMs}ms) ==={Environment.NewLine}");
        }
        catch { /* keep the thread alive even if the log isn't writable yet */ }

        _thread = new Thread(Loop)
        {
            IsBackground = true,
            Name = "UiFreezeWatchdog",
            Priority = ThreadPriority.AboveNormal,
        };
        _thread.Start();
    }

    public void Stop() => _stopRequested = true;

    private void Loop()
    {
        while (!_stopRequested)
        {
            try { RunOneIteration(); }
            catch { /* diagnostics are best-effort; never let this thread die */ }
        }
    }

    private void RunOneIteration()
    {
        var disp = _dispatcher;
        if (disp == null || disp.HasShutdownStarted || disp.HasShutdownFinished)
        {
            Thread.Sleep(500);
            return;
        }

        long pingPostedMs = _sw.ElapsedMilliseconds;
        try
        {
            disp.BeginInvoke(DispatcherPriority.Input, new Action(() =>
            {
                Interlocked.Exchange(ref _lastPingCompletedMs, _sw.ElapsedMilliseconds);
                UpdateActiveWindowSnapshot();
            }));
        }
        catch (InvalidOperationException) { Thread.Sleep(500); return; }

        while (!_stopRequested)
        {
            Thread.Sleep(PollIntervalMs);
            if (Interlocked.Read(ref _lastPingCompletedMs) >= pingPostedMs)
                return; // healthy round-trip
            if (_sw.ElapsedMilliseconds - pingPostedMs < FreezeThresholdMs)
                continue;

            // ── Freeze detected ── wait for the stuck ping to drain, then log the duration.
            string atWindow = _currentWindowTitle;
            long completedMs;
            while (!_stopRequested)
            {
                Thread.Sleep(PollIntervalMs);
                completedMs = Interlocked.Read(ref _lastPingCompletedMs);
                if (completedMs >= pingPostedMs)
                {
                    AppendLog($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] FREEZE {completedMs - pingPostedMs}ms  window={atWindow}");
                    return;
                }
            }
            return;
        }
    }

    private void UpdateActiveWindowSnapshot()
    {
        try
        {
            var app = Application.Current;
            if (app == null)
                return;
            Window? active = null;
            foreach (Window w in app.Windows)
                if (w.IsActive) { active = w; break; }
            _currentWindowTitle = active != null ? $"{active.GetType().Name}(\"{active.Title}\")" : "(none)";
        }
        catch { }
    }

    private void AppendLog(string line)
    {
        try
        {
            _logPath ??= Path.Combine(CrashLog.LogDir, "ui_freezes.log");
            File.AppendAllText(_logPath, line + Environment.NewLine);
        }
        catch { }
    }
}
