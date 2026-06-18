using EmuDOS.Core.Model;

namespace EmuDOS.Core.Catalog;

/// <summary>
/// One curated game in the catalog: a set of telltale filenames that identify it (Boxer's
/// approach) and the curated <see cref="GameProfile"/> to apply when those files are present.
/// </summary>
public sealed record CatalogEntry
{
    public required string Id { get; init; }

    public required string Title { get; init; }

    /// <summary>Distinctive filenames whose presence identifies this game (matched case-insensitively).</summary>
    public required IReadOnlyList<string> Telltales { get; init; }

    public required GameProfile Profile { get; init; }
}
