using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;

namespace EmuDOS.Services;

/// <summary>
/// Records unhandled exceptions to <c>crash.log</c> in the data folder's Logs directory, so a crash
/// (or a swallowed UI exception) can be diagnosed after the fact. Installed before anything else at
/// startup, and computes its path from LocalAppData directly so it works even if app services failed.
/// </summary>
internal static class CrashLog
{
    private static readonly object Gate = new();

    public static string LogDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EmuDOS", "Logs");

    public static void Install(Application app)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Write("AppDomain", e.ExceptionObject as Exception);

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Write("Task", e.Exception);
            e.SetObserved();
        };

        app.DispatcherUnhandledException += (_, e) =>
        {
            Write("Dispatcher", e.Exception);
            // Keep the app alive for a recoverable UI exception (it's logged); a truly fatal native
            // fault comes through AppDomain instead and tears the process down regardless.
            e.Handled = true;
        };
    }

    public static void Write(string source, Exception? ex)
    {
        if (ex is null)
            return;
        try
        {
            Directory.CreateDirectory(LogDir);
            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{source}] {ex}{Environment.NewLine}{Environment.NewLine}";
            lock (Gate)
                File.AppendAllText(Path.Combine(LogDir, "crash.log"), line);
        }
        catch { /* logging must never throw */ }
    }
}
