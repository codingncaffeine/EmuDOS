using System.Text.RegularExpressions;

namespace EmuDOS.Core.Engine.DosBoxPure;

/// <summary>
/// Sierra SCI games choose their music device in RESOURCE.CFG (the <c>soundDrv</c> line). When the
/// user selects MT-32 and the game ships the MT-32 driver, point <c>soundDrv</c> at it — the Boxer
/// "drop the ROMs and it just works" step, automated. No-op for non-Sierra games (no RESOURCE.CFG).
/// </summary>
public static partial class SierraSoundConfig
{
    [GeneratedRegex(@"(?im)^(\s*soundDrv\s*=\s*).*$")]
    private static partial Regex SoundDrvLine();

    /// <summary>Switch every applicable RESOURCE.CFG under <paramref name="contentDir"/> to MT32.DRV;
    /// returns the files changed (for logging). Idempotent and safe on any non-Sierra content.</summary>
    public static IReadOnlyList<string> EnsureMt32(string contentDir)
    {
        var changed = new List<string>();
        if (string.IsNullOrEmpty(contentDir) || !Directory.Exists(contentDir))
            return changed;

        IEnumerable<string> configs;
        try { configs = Directory.EnumerateFiles(contentDir, "RESOURCE.CFG", SearchOption.AllDirectories); }
        catch { return changed; }

        foreach (var cfg in configs)
        {
            try
            {
                var dir = Path.GetDirectoryName(cfg)!;
                if (!File.Exists(Path.Combine(dir, "MT32.DRV")))
                    continue; // this game doesn't ship the MT-32 driver

                var text = File.ReadAllText(cfg);
                var match = SoundDrvLine().Match(text);
                if (!match.Success || match.Value.Contains("MT32.DRV", StringComparison.OrdinalIgnoreCase))
                    continue; // no soundDrv line, or already MT-32

                File.WriteAllText(cfg, SoundDrvLine().Replace(text, match.Groups[1].Value + "MT32.DRV", 1));
                changed.Add(cfg);
            }
            catch
            {
                // Leave a config we can't read/write alone — never block a launch over it.
            }
        }

        return changed;
    }
}
