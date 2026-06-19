namespace EmuDOS.Core.Import;

/// <summary>How a freshly imported gamebox looks to us.</summary>
public enum ImportClassification
{
    /// <summary>A runnable game executable was found.</summary>
    ReadyToPlay,

    /// <summary>Only an installer was found — the game must be installed first.</summary>
    NeedsInstall,

    /// <summary>No executable found (e.g. raw disk images, or content we don't understand yet).</summary>
    Unknown,
}

/// <summary>Progress of an import (extraction is the slow part).</summary>
public readonly record struct ImportProgress(string Stage, double? Fraction);

/// <summary>Outcome of importing a folder/archive into a gamebox.</summary>
public sealed record ImportResult
{
    public required bool Success { get; init; }

    public string? GameboxPath { get; init; }

    public ImportClassification Classification { get; init; }

    /// <summary>All executables found in the content, as paths relative to the content root.</summary>
    public IReadOnlyList<string> Executables { get; init; } = [];

    /// <summary>The executable we'd launch (or the installer to run, when NeedsInstall).</summary>
    public string? ChosenExecutable { get; init; }

    public string? Error { get; init; }

    /// <summary>A non-fatal note to surface to the user (e.g. an unsupported disc format).</summary>
    public string? Warning { get; init; }
}
