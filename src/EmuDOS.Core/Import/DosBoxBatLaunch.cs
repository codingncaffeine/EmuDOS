using System.IO;
using System.Linq;
using EmuDOS.Core.Model;

namespace EmuDOS.Core.Import;

/// <summary>
/// Reads an eXoDOS-style per-game <c>DOSBOX.BAT</c> (the launcher shipped alongside the game) and turns
/// its body into launch pre-commands. These bats encode the tested launch recipe — mount the disc, cd
/// into the install, run the game (often the game binary lives on the CD) — which the heuristic
/// executable pick can't reproduce and frequently gets wrong, landing on a setup/config tool
/// (<c>ASK.COM</c>, a Sierra <c>Conf.exe</c>, …). Used whenever the bat mounts a disc OR runs a program;
/// a do-nothing bat (e.g. only <c>@CYCLES …</c>) is left to the normal executable selection.
/// </summary>
public static class DosBoxBatLaunch
{
    public static LaunchSpec? TryParse(string contentDir)
    {
        var path = Path.Combine(contentDir, "DOSBOX.BAT");
        if (!File.Exists(path))
            return null;

        var commands = new List<string>();
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim().TrimStart('@').Trim();
            if (line.Length == 0)
                continue;
            var lower = line.ToLowerInvariant();
            // Drop noise + dosbox-internal config (handled from the profile; emitting memory @config in
            // the autoexec can fault the game mid-start).
            if (lower is "cls" or "exit" or "pause" || lower.StartsWith("echo")
                || lower.StartsWith("rem ") || line.StartsWith("::") || lower.StartsWith("config "))
                continue;
            commands.Add(line);
        }

        bool meaningful = commands.Any(c =>
            c.StartsWith("IMGMOUNT", StringComparison.OrdinalIgnoreCase)
            || c.StartsWith("MOUNT ", StringComparison.OrdinalIgnoreCase)
            || RunsProgram(c));
        return meaningful && commands.Count > 0 ? new LaunchSpec { PreCommands = commands } : null;
    }

    // A command that runs a DOS program (any token ends in .exe/.com/.bat), e.g. "PQSWATForDOSBox.exe"
    // or "DOS4GW game.exe".
    private static bool RunsProgram(string command) =>
        command.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries)
            .Any(tok => tok.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                     || tok.EndsWith(".com", StringComparison.OrdinalIgnoreCase)
                     || tok.EndsWith(".bat", StringComparison.OrdinalIgnoreCase));
}
