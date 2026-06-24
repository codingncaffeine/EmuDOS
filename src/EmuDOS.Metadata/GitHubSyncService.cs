using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace EmuDOS.Metadata;

/// <summary>Device-flow login details to show the user.</summary>
public sealed record GitHubDeviceCode(string UserCode, string VerificationUri, string DeviceCode, int Interval, int ExpiresIn);

/// <summary>Outcome of a sync run.</summary>
public sealed record CloudSyncResult(int Uploaded, int Downloaded, string? Error)
{
    public bool Ok => Error is null;
}

/// <summary>
/// Syncs save data to a user's private GitHub repo via the OAuth device flow (no client secret).
/// Operates purely on paths so it carries no dependency on the rest of EmuDOS: per game it syncs the
/// save-state files (<c>state_*.sav.gz</c>/<c>.png</c>/<c>.json</c>, additive — they never overwrite),
/// <c>notes.md</c>, and pushes a gzip'd copy of the library database. Files are small, so it uses the
/// REST Contents API (with the Git blob API for downloads, which handles larger files).
/// </summary>
public sealed class GitHubSyncService
{
    private const string Api = "https://api.github.com";
    private static readonly HttpClient Http = CreateClient();
    private static readonly SemaphoreSlim Gate = new(1, 1); // only one sync at a time (launch + manual)

    // The GitHub Contents API rejects large blobs (and a huge base64 string overflows the JSON
    // serializer). Files above this are skipped during sync — mainly OS install .pure.zip overlays.
    private const long MaxUploadBytes = 40L * 1024 * 1024;

    private readonly Action<string> _log;
    private byte[]? _encKey; // set per SyncAsync; non-null = encrypt uploads / decrypt downloads
    private string _defaultBranch = "main"; // captured from the repo; used for recursive tree listing

    public GitHubSyncService(Action<string>? log = null) => _log = log ?? (_ => { });

    // Encrypt before upload (after gzip) when a key is set.
    private byte[] Wrap(byte[] data) => _encKey is null ? data : CloudCrypto.Encrypt(data, _encKey);

    // Decrypt a download: with a key, decrypt (null = wrong key); without one, skip encrypted blobs.
    private byte[]? Unwrap(byte[] data) =>
        _encKey is not null ? CloudCrypto.TryDecrypt(data, _encKey)
        : CloudCrypto.IsEncrypted(data) ? null : data;

    private static HttpClient CreateClient()
    {
        var c = new HttpClient();
        c.DefaultRequestHeaders.UserAgent.ParseAdd("EmuDOS");
        c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return c;
    }

    // ── Device-flow login ─────────────────────────────────────────────────────────────────
    public async Task<GitHubDeviceCode?> RequestDeviceCodeAsync(CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://github.com/login/device/code")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = Secrets.GitHubOAuthClientId,
                ["scope"] = "repo",
            }),
        };
        req.Headers.Accept.Clear();
        req.Headers.Accept.ParseAdd("application/json");
        using var resp = await Http.SendAsync(req, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            _log($"Device-code request failed: HTTP {(int)resp.StatusCode}");
            return null;
        }
        _log("Device code requested; awaiting authorization.");
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
        var r = doc.RootElement;
        return new GitHubDeviceCode(
            r.GetProperty("user_code").GetString() ?? "",
            r.GetProperty("verification_uri").GetString() ?? "https://github.com/login/device",
            r.GetProperty("device_code").GetString() ?? "",
            r.TryGetProperty("interval", out var iv) ? iv.GetInt32() : 5,
            r.TryGetProperty("expires_in", out var ex) ? ex.GetInt32() : 900);
    }

    /// <summary>Poll until the user authorizes (returns the token) or it times out/denies (null).</summary>
    public async Task<string?> PollAccessTokenAsync(GitHubDeviceCode code, CancellationToken ct = default)
    {
        var deadline = DateTime.UtcNow.AddSeconds(code.ExpiresIn);
        var interval = Math.Max(1, code.Interval);
        while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(interval), ct).ConfigureAwait(false);
            using var req = new HttpRequestMessage(HttpMethod.Post, "https://github.com/login/oauth/access_token")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"] = Secrets.GitHubOAuthClientId,
                    ["device_code"] = code.DeviceCode,
                    ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
                }),
            };
            req.Headers.Accept.Clear();
            req.Headers.Accept.ParseAdd("application/json");
            using var resp = await Http.SendAsync(req, ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
            var r = doc.RootElement;
            if (r.TryGetProperty("access_token", out var tok))
            {
                _log("Authorized — access token received.");
                return tok.GetString();
            }
            var err = r.TryGetProperty("error", out var e) ? e.GetString() : null;
            if (err == "slow_down")
                interval += 5;
            else if (err is not null and not "authorization_pending")
            {
                _log($"Authorization stopped: {err}");
                return null; // expired_token / access_denied
            }
        }
        _log("Authorization timed out.");
        return null;
    }

    public async Task<string?> GetLoginAsync(string token, CancellationToken ct = default)
    {
        using var resp = await SendAsync(HttpMethod.Get, $"{Api}/user", token, ct: ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            return null;
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
        return doc.RootElement.GetProperty("login").GetString();
    }

    // ── Sync ────────────────────────────────────────────────────────────────────────────
    public async Task<CloudSyncResult> SyncAsync(string token, string login, string repo,
        string gameboxesDir, string dbPath, IProgress<string>? progress = null, CancellationToken ct = default,
        byte[]? encKey = null)
    {
        _encKey = encKey;
        int up = 0, down = 0;
        if (!await Gate.WaitAsync(TimeSpan.FromMinutes(5), ct).ConfigureAwait(false))
            return new CloudSyncResult(0, 0, "Another sync is already running.");
        try
        {
            _log($"Sync started → {login}/{repo}.");
            progress?.Report("Connecting…");
            if (!await EnsureRepoAsync(token, login, repo, ct).ConfigureAwait(false))
            {
                _log("FAILED: could not access or create the sync repo.");
                return new CloudSyncResult(0, 0, "Couldn't access or create the sync repo.");
            }

            // Skip re-uploading files whose content hasn't changed since the last sync. gzip carries a
            // timestamp and encryption uses a random nonce, so the remote blob isn't comparable — track
            // a local hash of the RAW (pre-wrap) content instead. (Fixes re-pushing the same saves/DB
            // every launch.) The manifest is local-only, next to the DB.
            var manifestPath = Path.Combine(Path.GetDirectoryName(dbPath) ?? string.Empty, "cloud-sync-state.json");
            var manifest = LoadManifest(manifestPath);

            async Task<bool> PutIfChanged(string repoPath, byte[] rawForHash, Func<byte[]> makeUpload)
            {
                var hash = Sha256Hex(rawForHash);
                if (manifest.TryGetValue(repoPath, out var prev) && prev == hash)
                    return false; // unchanged since last sync
                var upload = makeUpload();
                if (upload.Length > MaxUploadBytes)
                {
                    // The GitHub Contents API can't take large blobs, and base64-ing one into the JSON
                    // request overflows the serializer. Skip it (e.g. a multi-hundred-MB OS .pure.zip)
                    // rather than aborting the whole sync.
                    _log($"Skipped {repoPath} — {upload.Length / (1024 * 1024)} MB is too large for cloud sync.");
                    return false;
                }
                if (await PutAsync(token, login, repo, repoPath, upload, ct).ConfigureAwait(false))
                {
                    manifest[repoPath] = hash;
                    return true;
                }
                return false;
            }

            // Push the library database (gzip'd snapshot). Read with shared access — SQLite keeps the
            // live DB file open, so a plain read would fail with "in use by another process".
            if (File.Exists(dbPath))
            {
                progress?.Report("Uploading library database…");
                var dbBytes = ReadShared(dbPath);
                if (await PutIfChanged("db/library.db.gz", dbBytes, () => Wrap(Gzip(dbBytes))).ConfigureAwait(false))
                {
                    up++;
                    _log("Uploaded library database.");
                }
            }

            // Per-game push: state files (additive) + notes.
            foreach (var gameDir in SafeDirs(gameboxesDir))
            {
                ct.ThrowIfCancellationRequested();
                var name = Path.GetFileName(gameDir);
                var savesDir = Path.Combine(gameDir, "saves");
                var stateFiles = Directory.Exists(savesDir)
                    ? Directory.EnumerateFiles(savesDir, "state_*").ToList()
                    : new List<string>();
                // dosbox_pure's Iso/OS save overlay (the writable union) — the in-game saves for disc
                // games live in here rather than in content/.
                var pureSaves = Directory.Exists(savesDir)
                    ? Directory.EnumerateFiles(savesDir, "*.pure.zip").ToList()
                    : new List<string>();
                var notes = Path.Combine(gameDir, "notes.md");
                var contentDir = Path.Combine(gameDir, "content");
                // In-game saves a folder game wrote in place (new/changed content since the import baseline).
                var ingame = EmuDOS.Core.Library.ContentBaseline.DiffSaves(contentDir, savesDir);
                if (stateFiles.Count == 0 && !File.Exists(notes) && ingame.Count == 0 && pureSaves.Count == 0)
                    continue;

                progress?.Report($"Syncing {name}…");
                var remote = await ListNamesAsync(token, login, repo, $"saves/{name}", ct).ConfigureAwait(false);
                int gameUp = 0;
                foreach (var f in stateFiles)
                {
                    if (remote.Contains(Path.GetFileName(f))) // additive: never overwrite an existing state
                        continue;
                    var stateBytes = Wrap(File.ReadAllBytes(f));
                    if (stateBytes.Length > MaxUploadBytes)
                    {
                        _log($"Skipped saves/{name}/{Path.GetFileName(f)} — {stateBytes.Length / (1024 * 1024)} MB is too large for cloud sync.");
                        continue;
                    }
                    if (await PutAsync(token, login, repo, $"saves/{name}/{Path.GetFileName(f)}", stateBytes, ct).ConfigureAwait(false))
                    { up++; gameUp++; }
                    else
                        _log($"WARN: upload failed for saves/{name}/{Path.GetFileName(f)}");
                }

                // In-game saves overwrite their cloud copy (they change as you play).
                int ingameUp = 0;
                foreach (var rel in ingame)
                {
                    var full = Path.Combine(contentDir, rel.Replace('/', Path.DirectorySeparatorChar));
                    if (!File.Exists(full))
                        continue;
                    var bytes = File.ReadAllBytes(full);
                    if (await PutIfChanged($"ingame/{name}/{rel}", bytes, () => Wrap(bytes)).ConfigureAwait(false))
                    { up++; ingameUp++; }
                }

                // Iso/OS save overlay: changes as you play, so overwrite the cloud copy when it differs.
                int pureUp = 0;
                foreach (var pure in pureSaves)
                {
                    var bytes = File.ReadAllBytes(pure);
                    if (await PutIfChanged($"saves/{name}/{Path.GetFileName(pure)}", bytes, () => Wrap(bytes)).ConfigureAwait(false))
                    { up++; pureUp++; }
                }

                bool notesUp = false;
                if (File.Exists(notes))
                {
                    var nb = File.ReadAllBytes(notes);
                    notesUp = await PutIfChanged($"notes/{name}.md", nb, () => Wrap(nb)).ConfigureAwait(false);
                    if (notesUp)
                        up++;
                }
                if (gameUp > 0 || ingameUp > 0 || pureUp > 0 || notesUp)
                    _log($"Pushed {name}: {gameUp} state(s), {ingameUp + pureUp} save(s){(notesUp ? " + notes" : "")}.");
            }

            SaveManifest(manifestPath, manifest);

            // Per-game pull: state files and notes that exist in the repo but not locally (additive,
            // only into games that exist locally).
            progress?.Report("Checking for saves to download…");
            foreach (var folder in await ListNamesAsync(token, login, repo, "saves", ct).ConfigureAwait(false))
            {
                var localSaves = Path.Combine(gameboxesDir, folder, "saves");
                if (!Directory.Exists(Path.Combine(gameboxesDir, folder)))
                    continue; // can't restore saves for a game that isn't installed here
                foreach (var (fname, sha) in await ListEntriesAsync(token, login, repo, $"saves/{folder}", ct).ConfigureAwait(false))
                {
                    var local = Path.Combine(localSaves, fname);
                    if (File.Exists(local))
                        continue;
                    var bytes = await GetBlobAsync(token, login, repo, sha, ct).ConfigureAwait(false);
                    if (bytes is null)
                        continue;
                    bytes = Unwrap(bytes);
                    if (bytes is null) // encrypted but no/wrong passphrase
                    {
                        _log($"Skipped (can't decrypt) saves/{folder}/{fname}");
                        continue;
                    }
                    Directory.CreateDirectory(localSaves);
                    File.WriteAllBytes(local, bytes);
                    down++;
                    _log($"Downloaded saves/{folder}/{fname}");
                }
            }

            // Pull in-game saves: ingame/{folder}/** into that game's content/, when missing locally.
            foreach (var (path, sha) in await GetTreeAsync(token, login, repo, ct).ConfigureAwait(false))
            {
                if (!path.StartsWith("ingame/", StringComparison.Ordinal))
                    continue;
                var rest = path["ingame/".Length..];
                var slash = rest.IndexOf('/');
                if (slash <= 0)
                    continue;
                var folder = rest[..slash];
                var rel = rest[(slash + 1)..];
                if (!Directory.Exists(Path.Combine(gameboxesDir, folder)))
                    continue; // game not installed here
                var local = Path.Combine(gameboxesDir, folder, "content", rel.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(local))
                    continue; // additive: don't clobber a local save with a possibly-older cloud copy
                var bytes = await GetBlobAsync(token, login, repo, sha, ct).ConfigureAwait(false);
                if (bytes is null)
                    continue;
                bytes = Unwrap(bytes);
                if (bytes is null)
                {
                    _log($"Skipped (can't decrypt) {path}");
                    continue;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(local)!);
                File.WriteAllBytes(local, bytes);
                down++;
                _log($"Downloaded {path}");
            }

            _log($"Sync finished — {up} uploaded, {down} downloaded.");
            progress?.Report($"Done — {up} uploaded, {down} downloaded.");
            return new CloudSyncResult(up, down, null);
        }
        catch (OperationCanceledException)
        {
            _log("Sync cancelled.");
            return new CloudSyncResult(up, down, "Cancelled.");
        }
        catch (Exception ex)
        {
            _log($"Sync ERROR: {ex.Message}");
            return new CloudSyncResult(up, down, ex.Message);
        }
        finally
        {
            Gate.Release();
        }
    }

    // ── GitHub REST helpers ───────────────────────────────────────────────────────────────
    private async Task<bool> EnsureRepoAsync(string token, string login, string repo, CancellationToken ct)
    {
        using (var get = await SendAsync(HttpMethod.Get, $"{Api}/repos/{login}/{repo}", token, ct: ct).ConfigureAwait(false))
            if (get.IsSuccessStatusCode)
            {
                _defaultBranch = await ReadDefaultBranch(get, ct).ConfigureAwait(false);
                return true;
            }

        var body = JsonSerializer.Serialize(new { name = repo, @private = true, auto_init = true });
        using var create = await SendAsync(HttpMethod.Post, $"{Api}/user/repos", token, body, ct).ConfigureAwait(false);
        if (!create.IsSuccessStatusCode)
            return false;
        _defaultBranch = await ReadDefaultBranch(create, ct).ConfigureAwait(false);
        return true;
    }

    private static async Task<string> ReadDefaultBranch(HttpResponseMessage resp, CancellationToken ct)
    {
        try
        {
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
            return doc.RootElement.TryGetProperty("default_branch", out var b) ? b.GetString() ?? "main" : "main";
        }
        catch
        {
            return "main";
        }
    }

    // All blob paths + shas in the repo, via the recursive Git tree (one call). Used to pull nested
    // in-game saves. Empty if the tree can't be read.
    private async Task<List<(string Path, string Sha)>> GetTreeAsync(string token, string login, string repo, CancellationToken ct)
    {
        var list = new List<(string, string)>();
        using var resp = await SendAsync(HttpMethod.Get,
            $"{Api}/repos/{login}/{repo}/git/trees/{_defaultBranch}?recursive=1", token, ct: ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            return list;
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
        if (!doc.RootElement.TryGetProperty("tree", out var tree) || tree.ValueKind != JsonValueKind.Array)
            return list;
        foreach (var item in tree.EnumerateArray())
            if (item.TryGetProperty("type", out var t) && t.GetString() == "blob")
                list.Add((item.GetProperty("path").GetString() ?? "", item.GetProperty("sha").GetString() ?? ""));
        return list;
    }

    private static async Task<bool> PutAsync(string token, string login, string repo, string path, byte[] content, CancellationToken ct)
    {
        if (content.LongLength > MaxUploadBytes)
            return false; // safety net: never base64 an oversized blob into the JSON request

        var sha = await GetShaAsync(token, login, repo, path, ct).ConfigureAwait(false);
        var payload = new Dictionary<string, object>
        {
            ["message"] = $"EmuDOS sync: {path}",
            ["content"] = Convert.ToBase64String(content),
        };
        if (sha is not null)
            payload["sha"] = sha;
        using var resp = await SendAsync(HttpMethod.Put, $"{Api}/repos/{login}/{repo}/contents/{EscapePath(path)}",
            token, JsonSerializer.Serialize(payload), ct).ConfigureAwait(false);
        return resp.IsSuccessStatusCode;
    }

    private static async Task<string?> GetShaAsync(string token, string login, string repo, string path, CancellationToken ct)
    {
        var dir = path.Contains('/') ? path[..path.LastIndexOf('/')] : "";
        var name = path[(path.LastIndexOf('/') + 1)..];
        foreach (var (fname, sha) in await ListEntriesAsync(token, login, repo, dir, ct).ConfigureAwait(false))
            if (fname == name)
                return sha;
        return null;
    }

    private static async Task<HashSet<string>> ListNamesAsync(string token, string login, string repo, string dir, CancellationToken ct)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (fname, _) in await ListEntriesAsync(token, login, repo, dir, ct).ConfigureAwait(false))
            set.Add(fname);
        return set;
    }

    private static async Task<List<(string Name, string Sha)>> ListEntriesAsync(string token, string login, string repo, string dir, CancellationToken ct)
    {
        var list = new List<(string, string)>();
        using var resp = await SendAsync(HttpMethod.Get, $"{Api}/repos/{login}/{repo}/contents/{EscapePath(dir)}", token, ct: ct).ConfigureAwait(false);
        if (resp.StatusCode == HttpStatusCode.NotFound || !resp.IsSuccessStatusCode)
            return list;
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
            return list;
        foreach (var item in doc.RootElement.EnumerateArray())
            if (item.GetProperty("type").GetString() == "file")
                list.Add((item.GetProperty("name").GetString() ?? "", item.GetProperty("sha").GetString() ?? ""));
        return list;
    }

    private static async Task<byte[]?> GetBlobAsync(string token, string login, string repo, string sha, CancellationToken ct)
    {
        using var resp = await SendAsync(HttpMethod.Get, $"{Api}/repos/{login}/{repo}/git/blobs/{sha}", token, ct: ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            return null;
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
        var content = doc.RootElement.GetProperty("content").GetString() ?? "";
        try { return Convert.FromBase64String(content.Replace("\n", "")); }
        catch { return null; }
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string token, string? jsonBody = null, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(method, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (jsonBody is not null)
            req.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        return await Http.SendAsync(req, ct).ConfigureAwait(false);
    }

    private static string EscapePath(string path) =>
        string.Join('/', path.Split('/').Select(Uri.EscapeDataString));

    private static string Sha256Hex(byte[] data) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(data));

    // Local-only record of the content hash last uploaded per repo path, so unchanged files are skipped.
    private static Dictionary<string, string> LoadManifest(string path)
    {
        try
        {
            if (File.Exists(path))
                return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))
                       ?? new(StringComparer.Ordinal);
        }
        catch { }
        return new(StringComparer.Ordinal);
    }

    private static void SaveManifest(string path, Dictionary<string, string> manifest)
    {
        try { File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(manifest)); }
        catch { /* the manifest is an optimization; losing it just means a re-upload next time */ }
    }

    // Read a file that another process may hold open (e.g. SQLite's live DB) by allowing shared R/W.
    private static byte[] ReadShared(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var ms = new MemoryStream();
        fs.CopyTo(ms);
        return ms.ToArray();
    }

    private static byte[] Gzip(byte[] data)
    {
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.Optimal, leaveOpen: true))
            gz.Write(data, 0, data.Length);
        return ms.ToArray();
    }

    private static IEnumerable<string> SafeDirs(string root) =>
        Directory.Exists(root) ? Directory.EnumerateDirectories(root) : Enumerable.Empty<string>();
}
