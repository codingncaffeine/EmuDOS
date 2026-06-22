using System.IO;
using System.Linq;
using EmuDOS.Core.Model;

namespace EmuDOS.Core.Import;

/// <summary>
/// Reads an eXoDOS-style per-game <c>DOSBOX.BAT</c> (the launcher shipped alongside the game) and turns
/// its body into launch pre-commands. These bats encode the tested launch recipe — mount the disc, cd
/// into the install, run the game (often the game binary lives on the CD) — which the heuristic
/// executable pick can't reproduce and frequently gets wrong (landing on a setup utility like
/// <c>ASK.COM</c>). Only used when the bat does a real mount, so simple single-exe games keep the
/// normal executable selection.
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
            if (lower is "cls" or "exit" or "pause" || lower.StartsWith("echo off")
                || lower.StartsWith("rem ") || line.StartsWith("::"))
                continue;
            commands.Add(line);
        }

        // Only take over the launch when the bat does a real mount (the CD/disc class the heuristic
        // gets wrong); otherwise leave the normal executable selection alone.
        var mounts = commands.Any(c => c.StartsWith("IMGMOUNT", StringComparison.OrdinalIgnoreCase)
                                    || c.StartsWith("MOUNT ", StringComparison.OrdinalIgnoreCase));
        return mounts && commands.Count > 0 ? new LaunchSpec { PreCommands = commands } : null;
    }
}
