using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace EmuDOS.Services;

/// <summary>The latest GitHub release, whether it's newer, and where to get it.</summary>
public sealed record AppUpdate(string Tag, string Url, string Notes, bool IsNewer,
                               string DownloadUrl = "", string? Digest = null);

/// <summary>
/// Checks the EmuDOS GitHub repo for the latest release and, on request, installs it. EmuDOS ships as a
/// single self-contained exe, so the install is a rename-on-restart self-replace (no separate updater):
/// download the new exe next to the running one, rename the running exe aside, move the new one into
/// place, relaunch, and exit. The leftover ".old" is swept on next startup. Modeled on Emutastic's
/// UpdateService (which uses a separate updater because it's framework-dependent and multi-file).
/// </summary>
public static class UpdateService
{
    private const string LatestApi = "https://api.github.com/repos/codingncaffeine/EmuDOS/releases/latest";

    /// <summary>The releases page to send the user to as a manual fallback.</summary>
    public const string ReleasesUrl = "https://github.com/codingncaffeine/EmuDOS/releases/latest";

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("EmuDOS/updater");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return http;
    }

    /// <summary>The running version as "Major.Minor.Build" (e.g. "0.5.5").</summary>
    public static string CurrentVersion
    {
        get
        {
            var v = Assembly.GetExecutingAssembly().GetName().Version;
            return v is null ? "0.0.0" : $"{v.Major}.{v.Minor}.{v.Build}";
        }
    }

    /// <summary>Fetch the latest release (with an IsNewer flag + download asset), or null if the check
    /// failed / there's no release. Never throws.</summary>
    public static async Task<AppUpdate?> LatestReleaseAsync(CancellationToken ct = default)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);

            using var resp = await Http.GetAsync(LatestApi, linked.Token).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                return null;

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(linked.Token).ConfigureAwait(false));
            var root = doc.RootElement;

            var tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
            if (string.IsNullOrWhiteSpace(tag))
                return null;

            var url = root.TryGetProperty("html_url", out var h) ? h.GetString() ?? ReleasesUrl : ReleasesUrl;
            var notes = root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";

            // The Windows zip asset.
            string downloadUrl = "";
            string? digest = null;
            if (root.TryGetProperty("assets", out var assets))
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    var name = asset.TryGetProperty("name", out var n) ? n.GetString() : null;
                    if (name is not null
                        && name.Contains("win-x64", StringComparison.OrdinalIgnoreCase)
                        && name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    {
                        downloadUrl = asset.TryGetProperty("browser_download_url", out var u) ? u.GetString() ?? "" : "";
                        digest = asset.TryGetProperty("digest", out var d) ? d.GetString() : null; // "sha256:…"
                        break;
                    }
                }
            }

            return new AppUpdate(tag, url, notes, IsNewer(tag), downloadUrl, digest);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Download, verify, install the update and restart. Throws with a user-readable message on
    /// failure (the existing install is left intact). On success it does not return — it restarts.</summary>
    public static async Task ApplyAsync(AppUpdate update, IProgress<string>? progress, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(update.DownloadUrl))
            throw new InvalidOperationException("No downloadable build is attached to this release — update from the releases page.");

        var exePath = Environment.ProcessPath
            ?? throw new InvalidOperationException("Couldn't locate the running EmuDOS.exe.");
        var exeDir = Path.GetDirectoryName(exePath)!;
        var exeName = Path.GetFileName(exePath);

        // Pre-flight: confirm we can write next to the exe (Program Files etc. would be read-only).
        progress?.Report("Checking permissions…");
        var probe = Path.Combine(exeDir, ".update-writetest");
        try { File.WriteAllText(probe, ""); File.Delete(probe); }
        catch (UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                "Can't update from this location — it's read-only. Download the latest release manually instead.");
        }

        // Download the zip.
        progress?.Report($"Downloading {update.Tag}…");
        var zipPath = Path.Combine(Path.GetTempPath(), $"EmuDOS-update-{update.Tag}.zip");
        using (var resp = await Http.GetAsync(update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
        {
            resp.EnsureSuccessStatusCode();
            var total = resp.Content.Headers.ContentLength ?? -1;
            long done = 0;
            await using var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await using var dst = File.Create(zipPath);
            var buf = new byte[81920];
            int read;
            while ((read = await src.ReadAsync(buf, ct).ConfigureAwait(false)) > 0)
            {
                await dst.WriteAsync(buf.AsMemory(0, read), ct).ConfigureAwait(false);
                done += read;
                if (total > 0)
                    progress?.Report($"Downloading {update.Tag}… {done * 100 / total}%");
            }
        }

        // Integrity gate against GitHub's published digest (when present) before we touch our install.
        if (!string.IsNullOrEmpty(update.Digest))
        {
            progress?.Report("Verifying…");
            var expected = update.Digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
                ? update.Digest[7..] : update.Digest;
            var actual = await Sha256HexAsync(zipPath, ct).ConfigureAwait(false);
            if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            {
                try { File.Delete(zipPath); } catch { }
                throw new InvalidOperationException(
                    "Update integrity check failed — the download didn't match its checksum, so nothing was installed.");
            }
        }

        // Extract the new exe alongside the current one (same volume → the swap is atomic renames).
        progress?.Report("Installing…");
        var newExe = Path.Combine(exeDir, exeName + ".new");
        try { if (File.Exists(newExe)) File.Delete(newExe); } catch { }
        using (var zip = ZipFile.OpenRead(zipPath))
        {
            var entry = zip.Entries.FirstOrDefault(e => string.Equals(e.Name, exeName, StringComparison.OrdinalIgnoreCase))
                ?? throw new FileNotFoundException($"The update is missing {exeName} — aborting.");
            entry.ExtractToFile(newExe, overwrite: true);
        }

        // Swap. A running exe can be renamed (Windows keeps it mapped), so move it aside, move the new
        // one in, and roll back if the second move fails so we never end up with no exe.
        var oldExe = Path.Combine(exeDir, exeName + ".old");
        try { if (File.Exists(oldExe)) File.Delete(oldExe); } catch { }
        File.Move(exePath, oldExe);
        try
        {
            File.Move(newExe, exePath);
        }
        catch
        {
            try { File.Move(oldExe, exePath); } catch { /* best-effort restore */ }
            throw new InvalidOperationException("Couldn't replace EmuDOS.exe — your install is unchanged.");
        }

        try { File.Delete(zipPath); } catch { }

        progress?.Report("Restarting…");
        // The awaits above used ConfigureAwait(false), so we're on a thread-pool thread now. Relaunch
        // and shut down on the UI thread — Application.Shutdown() must run there, or it throws a
        // cross-thread exception (even though the relaunch itself would still have happened).
        var app = System.Windows.Application.Current;
        app.Dispatcher.Invoke(() =>
        {
            Process.Start(new ProcessStartInfo(exePath) { UseShellExecute = true });
            app.Shutdown();
        });
    }

    /// <summary>Delete the leftover ".old"/".new" files from a previous self-update. Call at startup.</summary>
    public static void CleanupOldFiles()
    {
        try
        {
            var exePath = Environment.ProcessPath;
            if (exePath is null) return;
            var dir = Path.GetDirectoryName(exePath)!;
            foreach (var pattern in new[] { "*.old", "*.new" })
                foreach (var f in Directory.EnumerateFiles(dir, pattern))
                    try { File.Delete(f); } catch { }
        }
        catch { }
    }

    private static async Task<string> Sha256HexAsync(string path, CancellationToken ct)
    {
        await using var fs = File.OpenRead(path);
        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(fs, ct).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static bool IsNewer(string remoteTag)
    {
        var trimmed = remoteTag.TrimStart('v', 'V').Trim();
        if (!Version.TryParse(trimmed, out var remote))
            return false;
        var local = Assembly.GetExecutingAssembly().GetName().Version;
        if (local is null)
            return false;
        return new Version(remote.Major, remote.Minor, remote.Build)
             > new Version(local.Major, local.Minor, local.Build);
    }
}
