using System.Diagnostics;
using EmuDOS.Core.Downloads;
using EmuDOS.Core.Engine;
using EmuDOS.Core.Engine.DosBoxPure;
using EmuDOS.Core.Import;
using EmuDOS.Core.Infrastructure;
using EmuDOS.Core.Library;

namespace EmuDOS.Tests.Integration;

/// <summary>
/// Full real-game validation: download the core, import an actual game from
/// <c>EMUDOS_TEST_GAME</c> into a gamebox, then boot it and confirm frames flow. Opt-in via
/// <c>EMUDOS_LIVE_DOWNLOAD=1</c> + <c>EMUDOS_TEST_GAME=&lt;path&gt;</c>.
/// </summary>
public class GameLaunchLiveTests
{
    [Fact]
    public async Task Imports_and_boots_a_real_game()
    {
        var gamePath = Environment.GetEnvironmentVariable("EMUDOS_TEST_GAME");
        if (string.IsNullOrWhiteSpace(gamePath)
            || Environment.GetEnvironmentVariable("EMUDOS_LIVE_DOWNLOAD") != "1")
        {
            return;
        }

        var paths = new AppPaths(
            Path.Combine(Path.GetTempPath(), "emudos-realgame", Guid.NewGuid().ToString("N")));

        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
        var download = await new DownloadService(http, paths).DownloadAsync(AssetManifest.DosBoxPure);
        Assert.True(download.Success, download.Error);

        var store = new GameboxStore();
        var import = await new ImportPipeline(paths, store).ImportAsync(gamePath);
        Assert.True(import.Success, import.Error);
        Assert.Equal(ImportClassification.ReadyToPlay, import.Classification);

        var instance = store.Resolve(import.GameboxPath!);
        var host = new StubEngineHost();
        var engine = new DosBoxPureEngine(download.InstalledPath!, paths.SystemDir);
        using var session = engine.CreateSession(instance, host);

        session.Start();
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < TimeSpan.FromSeconds(25)
               && host.Frames < 5
               && session.State != EngineState.Faulted)
        {
            Thread.Sleep(100);
        }

        session.Stop();

        var error = (session as DosBoxPureSession)?.LastError;
        Assert.True(session.State != EngineState.Faulted, $"session faulted: {error}");
        Assert.True(host.Frames > 0, $"the game produced no frames; state={session.State}");
    }
}
