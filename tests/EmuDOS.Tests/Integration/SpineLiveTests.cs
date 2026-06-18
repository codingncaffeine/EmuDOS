using System.Diagnostics;
using EmuDOS.Core.Downloads;
using EmuDOS.Core.Engine;
using EmuDOS.Core.Engine.DosBoxPure;
using EmuDOS.Core.Infrastructure;
using EmuDOS.Core.Model;

namespace EmuDOS.Tests.Integration;

/// <summary>
/// Opt-in end-to-end validation of the whole M1 spine: download dosbox_pure, run a session
/// against an empty gamebox (the core boots to a DOS prompt), and confirm real frames flow
/// out through the host. Runs only when <c>EMUDOS_LIVE_DOWNLOAD=1</c>.
/// </summary>
public class SpineLiveTests
{
    [Fact]
    public async Task Spine_downloads_core_boots_and_produces_frames()
    {
        if (Environment.GetEnvironmentVariable("EMUDOS_LIVE_DOWNLOAD") != "1")
            return;

        var paths = new AppPaths(
            Path.Combine(Path.GetTempPath(), "emudos-spine", Guid.NewGuid().ToString("N")));

        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
        var download = await new DownloadService(http, paths).DownloadAsync(AssetManifest.DosBoxPure);
        Assert.True(download.Success, download.Error);

        var content = Path.Combine(paths.GameboxesDir, "boot");
        Directory.CreateDirectory(content);
        var instance = new GameInstance
        {
            Profile = new GameProfile { Title = "Boot" },
            GameboxPath = content,
            ContentPath = content,
            SavePath = paths.SavesDir,
        };

        var host = new StubEngineHost();
        var engine = new DosBoxPureEngine(download.InstalledPath!, paths.SystemDir);
        using var session = engine.CreateSession(instance, host);

        session.Start();
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < TimeSpan.FromSeconds(20)
               && host.Frames < 3
               && session.State != EngineState.Faulted)
        {
            Thread.Sleep(100);
        }

        session.Stop();

        var error = (session as DosBoxPureSession)?.LastError;
        Assert.True(session.State != EngineState.Faulted, $"session faulted: {error}");
        Assert.True(host.Frames > 0, $"core produced no frames; state={session.State}");
    }
}
