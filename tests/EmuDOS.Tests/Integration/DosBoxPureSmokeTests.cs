using System.Diagnostics;
using EmuDOS.Core.Engine.DosBoxPure;
using EmuDOS.Core.Model;

namespace EmuDOS.Tests.Integration;

/// <summary>
/// End-to-end validation of the M1 spine (GameProfile → adapter → libretro core → frames).
/// Gated on environment variables so it is a no-op on machines without a core + game:
/// <list type="bullet">
///   <item><c>EMUDOS_DOSBOX_PURE_DLL</c> — full path to dosbox_pure_libretro.dll</item>
///   <item><c>EMUDOS_TEST_CONTENT</c> — path to a game folder/zip to launch</item>
///   <item><c>EMUDOS_SYSTEM_DIR</c> — optional system dir (SoundFonts/ROMs)</item>
/// </list>
/// Run: set the vars, then <c>dotnet test</c>.
/// </summary>
public class DosBoxPureSmokeTests
{
    [Fact]
    public void Session_produces_frames_when_core_and_game_are_present()
    {
        var dll = Environment.GetEnvironmentVariable("EMUDOS_DOSBOX_PURE_DLL");
        var content = Environment.GetEnvironmentVariable("EMUDOS_TEST_CONTENT");
        if (string.IsNullOrWhiteSpace(dll) || string.IsNullOrWhiteSpace(content))
            return; // no core/game wired on this machine — nothing to validate

        var systemDir = Environment.GetEnvironmentVariable("EMUDOS_SYSTEM_DIR")
            ?? Path.GetTempPath();
        var savePath = Path.Combine(Path.GetTempPath(), "emudos-smoke", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(savePath);

        var instance = new GameInstance
        {
            Profile = new GameProfile { Title = "Smoke Test" },
            GameboxPath = content,
            ContentPath = content,
            SavePath = savePath,
        };

        var host = new StubEngineHost();
        var engine = new DosBoxPureEngine(dll, systemDir);
        using var session = engine.CreateSession(instance, host);

        session.Start();
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < TimeSpan.FromSeconds(5) && host.Frames < 10)
            Thread.Sleep(50);
        session.Stop();

        Assert.True(host.Frames > 0,
            $"Expected the core to produce frames; got {host.Frames}, state={session.State}.");
    }
}
