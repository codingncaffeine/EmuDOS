namespace EmuDOS.Core.Model;

/// <summary>
/// A resolved, ready-to-run game: an effective <see cref="GameProfile"/> bound to concrete
/// on-disk locations. This is what an <c>IDosEngine</c> is handed to launch.
/// </summary>
public sealed record GameInstance
{
    /// <summary>The effective profile (curated base merged with any user overrides).</summary>
    public required GameProfile Profile { get; init; }

    /// <summary>The gamebox root folder — the self-contained source of truth for this game.</summary>
    public required string GameboxPath { get; init; }

    /// <summary>The content root mounted as the primary (C:) drive.</summary>
    public required string ContentPath { get; init; }

    /// <summary>Directory where the engine reads/writes save data and save states.</summary>
    public required string SavePath { get; init; }
}
