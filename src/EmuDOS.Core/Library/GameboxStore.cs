using System.Text.Json;
using System.Text.Json.Serialization;
using EmuDOS.Core.Model;

namespace EmuDOS.Core.Library;

/// <summary>
/// Reads and writes the canonical <c>profile.json</c> in a gamebox, and resolves a gamebox
/// into a runnable <see cref="GameInstance"/>. Enums serialize as readable strings and the
/// file is indented, so a gamebox stays human-inspectable and hand-editable.
/// </summary>
public sealed class GameboxStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    public GameProfile ReadProfile(string gameboxRoot)
    {
        var box = new Gamebox(gameboxRoot);
        if (!box.Exists)
            throw new FileNotFoundException("Not a gamebox (no profile.json).", box.ProfilePath);

        using var stream = File.OpenRead(box.ProfilePath);
        return JsonSerializer.Deserialize<GameProfile>(stream, JsonOptions)
            ?? throw new InvalidDataException($"Invalid profile: {box.ProfilePath}");
    }

    public void WriteProfile(string gameboxRoot, GameProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var box = new Gamebox(gameboxRoot);
        Directory.CreateDirectory(box.Root);
        using var stream = File.Create(box.ProfilePath);
        JsonSerializer.Serialize(stream, profile, JsonOptions);
    }

    /// <summary>Descriptive metadata for the game card, or null if none has been fetched yet.</summary>
    public GameMetadata? ReadMetadata(string gameboxRoot)
    {
        var box = new Gamebox(gameboxRoot);
        if (!File.Exists(box.MetadataPath))
            return null;
        try
        {
            using var stream = File.OpenRead(box.MetadataPath);
            return JsonSerializer.Deserialize<GameMetadata>(stream, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public void WriteMetadata(string gameboxRoot, GameMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        var box = new Gamebox(gameboxRoot);
        Directory.CreateDirectory(box.Root);
        using var stream = File.Create(box.MetadataPath);
        JsonSerializer.Serialize(stream, metadata, JsonOptions);
    }

    /// <summary>Resolve a gamebox into a runnable instance, ensuring its content/saves dirs exist.</summary>
    public GameInstance Resolve(string gameboxRoot)
    {
        var box = new Gamebox(gameboxRoot);
        var profile = ReadProfile(gameboxRoot);
        Directory.CreateDirectory(box.ContentDir);
        Directory.CreateDirectory(box.SavesDir);
        return new GameInstance
        {
            Profile = profile,
            GameboxPath = box.Root,
            ContentPath = box.ContentDir,
            SavePath = box.SavesDir,
        };
    }

    /// <summary>Read per-game user state (window size, remembered exes); defaults if absent/invalid.</summary>
    public GameUserState ReadState(string gameboxRoot)
    {
        var box = new Gamebox(gameboxRoot);
        if (!File.Exists(box.StatePath))
            return new GameUserState();
        try
        {
            using var stream = File.OpenRead(box.StatePath);
            return JsonSerializer.Deserialize<GameUserState>(stream, JsonOptions) ?? new GameUserState();
        }
        catch
        {
            return new GameUserState();
        }
    }

    public void WriteState(string gameboxRoot, GameUserState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var box = new Gamebox(gameboxRoot);
        Directory.CreateDirectory(box.Root);
        using var stream = File.Create(box.StatePath);
        JsonSerializer.Serialize(stream, state, JsonOptions);
    }

    public bool IsGamebox(string directory) => new Gamebox(directory).Exists;

    /// <summary>Every gamebox under a gameboxes directory (the basis for rebuilding the index).</summary>
    public IEnumerable<string> EnumerateGameboxes(string gameboxesDir)
    {
        if (!Directory.Exists(gameboxesDir))
            yield break;

        foreach (var dir in Directory.EnumerateDirectories(gameboxesDir))
            if (IsGamebox(dir))
                yield return dir;
    }
}
