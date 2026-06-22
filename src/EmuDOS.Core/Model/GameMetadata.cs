namespace EmuDOS.Core.Model;

/// <summary>
/// Descriptive game metadata (from ScreenScraper), stored as <c>metadata.json</c> in the gamebox and
/// shown on the game card. Kept separate from the engine-config <c>profile.json</c>.
/// </summary>
public sealed record GameMetadata
{
    /// <summary>ScreenScraper's canonical game name (used to auto-correct ugly imported titles).</summary>
    public string? Name { get; init; }
    public string? Year { get; init; }
    public string? Developer { get; init; }
    public string? Publisher { get; init; }
    public string? Genre { get; init; }
    public string? Description { get; init; }

    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(Year) && string.IsNullOrWhiteSpace(Developer)
        && string.IsNullOrWhiteSpace(Publisher) && string.IsNullOrWhiteSpace(Genre)
        && string.IsNullOrWhiteSpace(Description);
}
