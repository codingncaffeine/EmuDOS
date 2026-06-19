using System.Globalization;

namespace EmuDOS.Core.Infrastructure;

/// <summary>
/// Minimal append-only logger writing timestamped lines to a file under the data folder's
/// Logs directory (mirrors Emutastic's per-feature logs).
/// </summary>
public sealed class AppLog
{
    private readonly string _path;
    private readonly Lock _gate = new();

    public AppLog(AppPaths paths, string fileName)
    {
        var dir = Path.Combine(paths.DataRoot, "Logs");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, fileName);
    }

    public void Info(string message) => Write("INFO", message);

    public void Error(string message) => Write("ERROR", message);

    private void Write(string level, string message)
    {
        var line = string.Format(CultureInfo.InvariantCulture,
            "[{0:yyyy-MM-dd HH:mm:ss}] [{1}] {2}", DateTime.Now, level, message);
        lock (_gate)
        {
            try { File.AppendAllText(_path, line + Environment.NewLine); }
            catch { /* logging must never throw */ }
        }
    }
}
