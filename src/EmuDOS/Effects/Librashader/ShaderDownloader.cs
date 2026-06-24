using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace EmuDOS.Effects.Librashader;

/// <summary>Downloads the libretro slang shader pack and the librashader runtime DLL into the data
/// folder, so the CRT shader picker has presets and an engine to run them.</summary>
public static class ShaderDownloader
{
    private const string SlangPackUrl = "https://buildbot.libretro.com/assets/frontend/shaders_slang.zip";
    private const string RuntimeUrl =
        "https://github.com/SnowflakePowered/librashader/releases/download/librashader-v0.6.2/librashader-x86_64-windows-0.6.1-optimized.zip";

    public static bool IsInstalled(string slangRoot, string dllPath)
        => File.Exists(Path.Combine(slangRoot, ".installed")) && File.Exists(dllPath);

    /// <summary>Download + install the shader pack and librashader.dll. Reports coarse progress text;
    /// throws on failure.</summary>
    public static async Task DownloadAsync(string slangRoot, string dllPath, Action<string>? progress = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dllPath)!);
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(20) };
        http.DefaultRequestHeaders.Add("User-Agent", "EmuDOS");

        // 1. Slang preset pack (~2500 .slangp). Extract to temp, then move the real root into place so
        //    we don't depend on whether the zip wraps everything in one top-level folder.
        progress?.Invoke("Downloading shader pack…");
        var packZip = Path.Combine(Path.GetTempPath(), "emudos_shaders_slang.zip");
        await DownloadFileAsync(http, SlangPackUrl, packZip);

        progress?.Invoke("Extracting shader pack…");
        var temp = Path.Combine(Path.GetTempPath(), "emudos_slang_" + Guid.NewGuid().ToString("N"));
        ZipFile.ExtractToDirectory(packZip, temp);
        var realRoot = temp;
        while (Directory.GetFiles(realRoot).Length == 0 && Directory.GetDirectories(realRoot).Length == 1)
            realRoot = Directory.GetDirectories(realRoot)[0]; // descend single wrapper folders

        if (Directory.Exists(slangRoot))
            Directory.Delete(slangRoot, recursive: true);
        Directory.CreateDirectory(Path.GetDirectoryName(slangRoot)!);
        try { Directory.Move(realRoot, slangRoot); }
        catch { CopyDirectory(realRoot, slangRoot); } // cross-volume fallback
        TryDelete(packZip);
        TryDeleteDir(temp);

        // 2. librashader runtime DLL (from its release zip).
        progress?.Invoke("Downloading shader engine…");
        var rtZip = Path.Combine(Path.GetTempPath(), "emudos_librashader_rt.zip");
        await DownloadFileAsync(http, RuntimeUrl, rtZip);
        using (var z = ZipFile.OpenRead(rtZip))
        {
            var entry = z.Entries.FirstOrDefault(e => e.Name.Equals("librashader.dll", StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException("librashader.dll not found in the runtime package.");
            entry.ExtractToFile(dllPath, overwrite: true);
        }
        TryDelete(rtZip);

        File.WriteAllText(Path.Combine(slangRoot, ".installed"), DateTime.UtcNow.ToString("o"));
        progress?.Invoke("Shaders installed.");
    }

    private static async Task DownloadFileAsync(HttpClient http, string url, string dest)
    {
        using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        resp.EnsureSuccessStatusCode();
        await using var src = await resp.Content.ReadAsStreamAsync();
        await using var fs = File.Create(dest);
        await src.CopyToAsync(fs);
    }

    private static void CopyDirectory(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var dir in Directory.GetDirectories(from, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(dir.Replace(from, to));
        foreach (var file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
            File.Copy(file, file.Replace(from, to), overwrite: true);
    }

    private static void TryDelete(string path) { try { File.Delete(path); } catch { } }
    private static void TryDeleteDir(string path) { try { Directory.Delete(path, true); } catch { } }
}
