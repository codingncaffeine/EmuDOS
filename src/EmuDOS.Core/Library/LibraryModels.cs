namespace EmuDOS.Core.Library;

/// <summary>Kind of media associated with a game.</summary>
public enum MediaKind
{
    BoxFront,
    BoxBack,
    Screenshot,
    Logo,
    Manual,
    Other,
}

/// <summary>A game as indexed in the library DB (a row over a gamebox).</summary>
public sealed record LibraryGame
{
    public required long Id { get; init; }

    /// <summary>Absolute path to the gamebox folder (stored relative to DataRoot).</summary>
    public required string GameboxPath { get; init; }

    public required string Title { get; init; }

    public string? CanonicalId { get; init; }

    public string? Executable { get; init; }

    public string? Machine { get; init; }

    public DateTimeOffset DateAdded { get; init; }

    public DateTimeOffset? LastPlayed { get; init; }

    public int PlayCount { get; init; }

    public bool IsFavorite { get; init; }

    /// <summary>Total seconds the game has been played (summed across sessions).</summary>
    public long TotalPlayTimeSeconds { get; init; }
}

/// <summary>A media file (art/manual/…) linked to a game.</summary>
public sealed record MediaItem
{
    public required long Id { get; init; }

    public required long GameId { get; init; }

    public required MediaKind Kind { get; init; }

    /// <summary>Absolute path to the media file (stored relative to DataRoot).</summary>
    public required string Path { get; init; }

    public string? Source { get; init; }
}
