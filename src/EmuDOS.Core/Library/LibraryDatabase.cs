using System.Globalization;
using EmuDOS.Core.Infrastructure;
using Microsoft.Data.Sqlite;

namespace EmuDOS.Core.Library;

/// <summary>
/// The library: a SQLite index over the gameboxes. It is rebuildable — gameboxes are the
/// source of truth, so deleting the DB and re-syncing restores the library (only stats are
/// DB-only). Schema evolves through an ordered <c>PRAGMA user_version</c> migration runner,
/// and all paths are stored relative to DataRoot so the data folder stays portable.
/// </summary>
public sealed class LibraryDatabase
{
    private readonly AppPaths _paths;
    private readonly GameboxStore _store;
    private readonly string _connectionString;

    public LibraryDatabase(AppPaths paths, GameboxStore store, string? dbPath = null)
    {
        _paths = paths;
        _store = store;
        var path = dbPath ?? Path.Combine(paths.DataRoot, "library.db");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        ApplyPendingRestore(path); // a Backups-tab "restore" stages a file applied here, before the DB opens
        _connectionString = new SqliteConnectionStringBuilder { DataSource = path }.ToString();

        using var connection = Open();
        Migrate(connection);
    }

    /// <summary>The path a Backups-tab restore writes to; applied over the DB on next launch (the live
    /// DB is locked while running, so a restore can't overwrite it in place).</summary>
    public static string PendingRestorePath(AppPaths paths) => Path.Combine(paths.DataRoot, "library.db.restore");

    private static void ApplyPendingRestore(string dbPath)
    {
        var pending = dbPath + ".restore";
        if (!File.Exists(pending))
            return;
        try
        {
            File.Copy(pending, dbPath, overwrite: true);
            File.Delete(pending);
        }
        catch { /* leave it staged; retry next launch */ }
    }

    /// <summary>Add or refresh a game from its gamebox. Stats are preserved on refresh.</summary>
    public LibraryGame UpsertFromGamebox(string gameboxPath)
    {
        var profile = _store.ReadProfile(gameboxPath);
        var json = File.ReadAllText(new Gamebox(gameboxPath).ProfilePath);

        using var connection = Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO Games (GameboxPath, Title, CanonicalId, Executable, Machine, ProfileJson, DateAdded)
            VALUES ($path, $title, $cid, $exe, $machine, $json, $now)
            ON CONFLICT(GameboxPath) DO UPDATE SET
                Title = excluded.Title,
                CanonicalId = excluded.CanonicalId,
                Executable = excluded.Executable,
                Machine = excluded.Machine,
                ProfileJson = excluded.ProfileJson;
            """;
        Bind(cmd, "$path", ToStorage(gameboxPath));
        Bind(cmd, "$title", profile.Title);
        Bind(cmd, "$cid", profile.CanonicalId);
        Bind(cmd, "$exe", profile.Launch.Executable);
        Bind(cmd, "$machine", profile.Machine.Machine.ToString());
        Bind(cmd, "$json", json);
        Bind(cmd, "$now", Now());
        cmd.ExecuteNonQuery();

        return GetByPath(connection, gameboxPath)!;
    }

    public IReadOnlyList<LibraryGame> GetGames()
    {
        using var connection = Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT {GameColumns} FROM Games ORDER BY Title COLLATE NOCASE;";
        using var reader = cmd.ExecuteReader();
        var games = new List<LibraryGame>();
        while (reader.Read())
            games.Add(MapGame(reader));
        return games;
    }

    /// <summary>
    /// Bring the index in line with the gameboxes on disk: refresh/add every gamebox and prune
    /// rows whose gamebox is gone. Returns the number of gameboxes synced.
    /// </summary>
    public int SyncFromGameboxes()
    {
        var boxes = _store.EnumerateGameboxes(_paths.GameboxesDir).ToList();
        foreach (var box in boxes)
            UpsertFromGamebox(box);

        var keep = boxes.Select(ToStorage).ToHashSet(StringComparer.OrdinalIgnoreCase);
        using var connection = Open();
        using (var read = connection.CreateCommand())
        {
            read.CommandText = "SELECT Id, GameboxPath FROM Games;";
            var orphans = new List<long>();
            using (var reader = read.ExecuteReader())
            {
                while (reader.Read())
                    if (!keep.Contains(reader.GetString(1)))
                        orphans.Add(reader.GetInt64(0));
            }

            foreach (var id in orphans)
            {
                using var del = connection.CreateCommand();
                del.CommandText = "DELETE FROM Games WHERE Id = $id;";
                Bind(del, "$id", id);
                del.ExecuteNonQuery();
            }
        }

        return boxes.Count;
    }

    /// <summary>Remove a game from the index (its Media rows cascade). The gamebox on disk is
    /// deleted separately by the caller.</summary>
    public void Remove(long gameId)
    {
        using var connection = Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM Games WHERE Id = $id;";
        Bind(cmd, "$id", gameId);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Add to a game's total play-time (called at session end). No-op for non-positive seconds.</summary>
    public void AddPlayTime(long gameId, int seconds)
    {
        if (seconds <= 0)
            return;
        using var connection = Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "UPDATE Games SET TotalPlayTimeSeconds = TotalPlayTimeSeconds + $sec WHERE Id = $id;";
        Bind(cmd, "$sec", seconds);
        Bind(cmd, "$id", gameId);
        cmd.ExecuteNonQuery();
    }

    /// <summary>A single game by id (fresh stats), or null if it's gone.</summary>
    public LibraryGame? GetGame(long id)
    {
        using var connection = Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT {GameColumns} FROM Games WHERE Id = $id;";
        Bind(cmd, "$id", id);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? MapGame(reader) : null;
    }

    public void RecordPlay(long gameId)
    {
        using var connection = Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "UPDATE Games SET PlayCount = PlayCount + 1, LastPlayed = $now WHERE Id = $id;";
        Bind(cmd, "$now", Now());
        Bind(cmd, "$id", gameId);
        cmd.ExecuteNonQuery();
    }

    public void SetFavorite(long gameId, bool favorite)
    {
        using var connection = Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "UPDATE Games SET IsFavorite = $fav WHERE Id = $id;";
        Bind(cmd, "$fav", favorite ? 1 : 0);
        Bind(cmd, "$id", gameId);
        cmd.ExecuteNonQuery();
    }

    public void AddMedia(long gameId, MediaKind kind, string absolutePath, string? source = null)
    {
        using var connection = Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO Media (GameId, Kind, Path, Source) VALUES ($g, $k, $p, $s);
            """;
        Bind(cmd, "$g", gameId);
        Bind(cmd, "$k", kind.ToString());
        Bind(cmd, "$p", ToStorage(absolutePath));
        Bind(cmd, "$s", source);
        cmd.ExecuteNonQuery();
    }

    public IReadOnlyList<MediaItem> GetMedia(long gameId)
    {
        using var connection = Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT Id, GameId, Kind, Path, Source FROM Media WHERE GameId = $g;";
        Bind(cmd, "$g", gameId);
        using var reader = cmd.ExecuteReader();
        var items = new List<MediaItem>();
        while (reader.Read())
        {
            items.Add(new MediaItem
            {
                Id = reader.GetInt64(0),
                GameId = reader.GetInt64(1),
                Kind = Enum.Parse<MediaKind>(reader.GetString(2)),
                Path = FromStorage(reader.GetString(3)),
                Source = reader.IsDBNull(4) ? null : reader.GetString(4),
            });
        }

        return items;
    }

    private const string GameColumns =
        "Id, GameboxPath, Title, CanonicalId, Executable, Machine, DateAdded, LastPlayed, PlayCount, IsFavorite, TotalPlayTimeSeconds";

    private LibraryGame? GetByPath(SqliteConnection connection, string gameboxPath)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT {GameColumns} FROM Games WHERE GameboxPath = $path;";
        Bind(cmd, "$path", ToStorage(gameboxPath));
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? MapGame(reader) : null;
    }

    private LibraryGame MapGame(SqliteDataReader r) => new()
    {
        Id = r.GetInt64(0),
        GameboxPath = FromStorage(r.GetString(1)),
        Title = r.GetString(2),
        CanonicalId = r.IsDBNull(3) ? null : r.GetString(3),
        Executable = r.IsDBNull(4) ? null : r.GetString(4),
        Machine = r.IsDBNull(5) ? null : r.GetString(5),
        DateAdded = ParseDate(r.GetString(6)),
        LastPlayed = r.IsDBNull(7) ? null : ParseDate(r.GetString(7)),
        PlayCount = r.GetInt32(8),
        IsFavorite = r.GetInt32(9) != 0,
        TotalPlayTimeSeconds = r.GetInt64(10),
    };

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys = ON;";
        pragma.ExecuteNonQuery();
        return connection;
    }

    private static void Migrate(SqliteConnection connection)
    {
        if (UserVersion(connection) < 1)
        {
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = """
                    CREATE TABLE Games (
                        Id          INTEGER PRIMARY KEY AUTOINCREMENT,
                        GameboxPath TEXT NOT NULL UNIQUE,
                        Title       TEXT NOT NULL,
                        CanonicalId TEXT,
                        Executable  TEXT,
                        Machine     TEXT,
                        ProfileJson TEXT NOT NULL,
                        DateAdded   TEXT NOT NULL,
                        LastPlayed  TEXT,
                        PlayCount   INTEGER NOT NULL DEFAULT 0,
                        IsFavorite  INTEGER NOT NULL DEFAULT 0
                    );

                    CREATE TABLE Media (
                        Id     INTEGER PRIMARY KEY AUTOINCREMENT,
                        GameId INTEGER NOT NULL,
                        Kind   TEXT NOT NULL,
                        Path   TEXT NOT NULL,
                        Source TEXT,
                        FOREIGN KEY(GameId) REFERENCES Games(Id) ON DELETE CASCADE
                    );

                    CREATE INDEX idx_media_game ON Media(GameId);
                    """;
                cmd.ExecuteNonQuery();
            }

            SetUserVersion(connection, 1);
        }

        if (UserVersion(connection) < 2)
        {
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "ALTER TABLE Games ADD COLUMN TotalPlayTimeSeconds INTEGER NOT NULL DEFAULT 0;";
                cmd.ExecuteNonQuery();
            }

            SetUserVersion(connection, 2);
        }
    }

    private static long UserVersion(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "PRAGMA user_version;";
        return (long)(cmd.ExecuteScalar() ?? 0L);
    }

    private static void SetUserVersion(SqliteConnection connection, int version)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"PRAGMA user_version = {version};"; // int literal; not parameterizable
        cmd.ExecuteNonQuery();
    }

    private string ToStorage(string absolutePath) =>
        Path.GetRelativePath(_paths.DataRoot, absolutePath);

    private string FromStorage(string relativePath) =>
        Path.GetFullPath(Path.Combine(_paths.DataRoot, relativePath));

    private static string Now() => DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseDate(string s) =>
        DateTimeOffset.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private static void Bind(SqliteCommand cmd, string name, object? value) =>
        cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
}
