using EmuDOS.Core.Downloads;
using EmuDOS.Core.Infrastructure;

namespace EmuDOS.Tests.Integration;

/// <summary>
/// Opt-in network test: actually downloads dosbox_pure from the libretro buildbot through
/// the real <see cref="DownloadService"/>. Runs only when <c>EMUDOS_LIVE_DOWNLOAD=1</c>, so
/// it never breaks offline/CI runs. Validates the manifest URL and the zip→dll install path.
/// </summary>
public class DownloadLiveTests
{
    [Fact]
    public async Task Buildbot_download_installs_a_real_core_dll()
    {
        if (Environment.GetEnvironmentVariable("EMUDOS_LIVE_DOWNLOAD") != "1")
            return;

        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
        var paths = new AppPaths(
            Path.Combine(Path.GetTempPath(), "emudos-live", Guid.NewGuid().ToString("N")));
        var service = new DownloadService(http, paths);

        var result = await service.DownloadAsync(AssetManifest.DosBoxPure);

        Assert.True(result.Success, result.Error);
        var bytes = await File.ReadAllBytesAsync(result.InstalledPath!);
        Assert.True(bytes.Length > 100_000, $"core DLL too small: {bytes.Length} bytes");
        Assert.Equal(0x4D, bytes[0]); // 'M'
        Assert.Equal(0x5A, bytes[1]); // 'Z' — valid PE header
    }
}
