using EmuDOS.Core.Model;

namespace EmuDOS.Core.Catalog;

/// <summary>
/// Decides the effective profile for a game by layering: a curated catalog match provides the
/// base config, the imported baseline keeps the game's identity, and a user override (once the
/// user edits) locks the profile so auto-enrichment never clobbers their changes.
/// </summary>
public sealed class ProfileResolver(CatalogDatabase catalog)
{
    /// <summary>
    /// Enrich an imported <paramref name="baseline"/> with curated config when the catalog
    /// recognizes the content. A profile the user has taken over is returned untouched.
    /// </summary>
    public GameProfile Resolve(GameProfile baseline, IEnumerable<string> contentFileNames)
    {
        ArgumentNullException.ThrowIfNull(baseline);

        // The user has the final say — never overwrite their edits.
        if (baseline.Origin == ProfileOrigin.UserOverride)
            return baseline;

        // Prefer a content/telltale match; fall back to the game's title (how eXoDOS entries
        // are usually keyed, since their launch exe is hidden behind a run.bat).
        var curated = catalog.Match(contentFileNames) ?? catalog.MatchByTitle(baseline.Title);
        if (curated is null)
            return baseline;

        // Curated config wins; the gamebox keeps its own identity and mounts. Only fall back to
        // a detected executable if the curated profile specifies no way to launch.
        var launch = curated.Launch.Executable is null && curated.Launch.PreCommands.Count == 0
            ? curated.Launch with { Executable = baseline.Launch.Executable }
            : curated.Launch;

        return curated with
        {
            Title = string.IsNullOrWhiteSpace(baseline.Title) ? curated.Title : baseline.Title,
            CanonicalId = baseline.CanonicalId ?? curated.CanonicalId,
            SourceMedia = baseline.SourceMedia,
            Mounts = baseline.Mounts.Count > 0 ? baseline.Mounts : curated.Mounts,
            Launch = launch,
            Origin = ProfileOrigin.CuratedBase,
        };
    }
}
