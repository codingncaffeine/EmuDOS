using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using EmuDOS.Core.Downloads;
using EmuDOS.Core.Infrastructure;

namespace EmuDOS.Tests;

public class DownloadServiceTests
{
    [Fact]
    public void AppPaths_resolves_and_creates_subdirectories()
    {
        var root = TempRoot();
        var paths = new AppPaths(root);

        Assert.Equal(Path.Combine(root, "Cores"), paths.CoresDir);
        Assert.Equal(Path.Combine(root, "System"), paths.SystemDir);
        Assert.Equal(Path.Combine(root, "Catalog"), paths.CatalogDir);
        Assert.True(Directory.Exists(paths.CoresDir));
        Assert.True(Directory.Exists(paths.CatalogDir));
    }

    [Fact]
    public void Manifest_describes_the_dosbox_pure_core()
    {
        var asset = AssetManifest.DosBoxPure;

        Assert.Equal("dosbox_pure", asset.Id);
        Assert.Equal(AssetCategory.Core, asset.Category);
        Assert.Equal(DownloadKind.ZippedCore, asset.Kind);
        Assert.EndsWith("dosbox_pure_libretro.dll.zip", asset.Url);
        Assert.Contains(AssetManifest.All, a => a.Id == "dosbox_pure");
    }

    [Fact]
    public async Task Downloads_and_extracts_a_zipped_core()
    {
        var dll = new byte[] { 1, 2, 3, 4, 5 };
        using var http = new HttpClient(new FakeHttpMessageHandler(MakeZip("dosbox_pure_libretro.dll", dll)));
        var paths = new AppPaths(TempRoot());
        var service = new DownloadService(http, paths);
        var asset = AssetManifest.DosBoxPure;

        Assert.False(service.IsInstalled(asset));

        var result = await service.DownloadAsync(asset);

        Assert.True(result.Success, result.Error);
        Assert.True(service.IsInstalled(asset));
        Assert.Equal(Path.Combine(paths.CoresDir, "dosbox_pure_libretro.dll"), result.InstalledPath);
        Assert.Equal(dll, await File.ReadAllBytesAsync(result.InstalledPath!));
        Assert.Equal(Convert.ToHexString(SHA256.HashData(dll)), result.Sha256);
    }

    [Fact]
    public async Task Downloads_a_plain_file_to_the_system_directory()
    {
        var bytes = new byte[] { 7, 7, 7 };
        using var http = new HttpClient(new FakeHttpMessageHandler(bytes));
        var paths = new AppPaths(TempRoot());
        var service = new DownloadService(http, paths);
        var asset = new DownloadAsset
        {
            Id = "gm",
            DisplayName = "GM SoundFont",
            Url = "https://example/gm.sf2",
            Kind = DownloadKind.File,
            FileName = "gm.sf2",
            Category = AssetCategory.SoundFont,
        };

        var result = await service.DownloadAsync(asset);

        Assert.True(result.Success, result.Error);
        Assert.Equal(Path.Combine(paths.SystemDir, "gm.sf2"), result.InstalledPath);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(result.InstalledPath!));
    }

    [Fact]
    public async Task Pinned_checksum_mismatch_fails_and_removes_file()
    {
        using var http = new HttpClient(new FakeHttpMessageHandler(MakeZip("x.dll", [9])));
        var paths = new AppPaths(TempRoot());
        var service = new DownloadService(http, paths);
        var asset = new DownloadAsset
        {
            Id = "x",
            DisplayName = "x",
            Url = "https://example/x.zip",
            Kind = DownloadKind.ZippedCore,
            FileName = "x.dll",
            Category = AssetCategory.Core,
            Sha256 = "00DEADBEEF",
        };

        var result = await service.DownloadAsync(asset);

        Assert.False(result.Success);
        Assert.False(File.Exists(Path.Combine(paths.CoresDir, "x.dll")));
    }

    [Fact]
    public async Task Http_failure_returns_error_not_exception()
    {
        using var http = new HttpClient(new FakeHttpMessageHandler([], HttpStatusCode.NotFound));
        var service = new DownloadService(http, new AppPaths(TempRoot()));

        var result = await service.DownloadAsync(AssetManifest.DosBoxPure);

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
    }

    private static string TempRoot() =>
        Path.Combine(Path.GetTempPath(), "emudos-tests", Guid.NewGuid().ToString("N"));

    private static byte[] MakeZip(string entryName, byte[] content)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = zip.CreateEntry(entryName);
            using var s = entry.Open();
            s.Write(content);
        }

        return ms.ToArray();
    }

    private sealed class FakeHttpMessageHandler(byte[] payload, HttpStatusCode status = HttpStatusCode.OK)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new ByteArrayContent(payload) });
    }
}
