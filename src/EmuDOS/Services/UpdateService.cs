using System;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace EmuDOS.Services;

/// <summary>The latest GitHub release, and whether it's newer than what's running.</summary>
public sealed record AppUpdate(string Tag, string Url, string Notes, bool IsNewer);

/// <summary>
/// Checks the EmuDOS GitHub repo for the latest release and compares it to the running version.
/// Detection only — applying is left to the user (open the releases page). Modeled on Emutastic's
/// UpdateService.
/// </summary>
public static class UpdateService
{
    private const string LatestApi = "https://api.github.com/repos/codingncaffeine/EmuDOS/releases/latest";

    /// <summary>The releases page to send the user to for the download.</summary>
    public const string ReleasesUrl = "https://github.com/codingncaffeine/EmuDOS/releases/latest";

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
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

    /// <summary>Fetch the latest release (with an IsNewer flag), or null if the check failed / there's
    /// no release. Never throws.</summary>
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
            return new AppUpdate(tag, url, notes, IsNewer(tag));
        }
        catch
        {
            return null;
        }
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
