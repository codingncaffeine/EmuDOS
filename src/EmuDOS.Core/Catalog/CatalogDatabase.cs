using System.Text.Json;
using System.Text.Json.Serialization;
using EmuDOS.Core.Model;
using Microsoft.Data.Sqlite;

namespace EmuDOS.Core.Catalog;

/// <summary>
/// The curated config catalog: maps a game's telltale files to its curated profile. Shipped
/// as an embedded baseline and updatable as a download. Matching follows Boxer — a game
/// matches when ALL of an entry's telltales are present in the content; the most specific
/// (most telltales) wins.
/// </summary>
public sealed class CatalogDatabase
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _connectionString;

    public CatalogDatabase(string dbPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(dbPath))!);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString();
        using var connection = Open();
        Initialize(connection);
    }

    public int Count
    {
        get
        {
            using var connection = Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM CatalogEntries;";
            return (int)(long)(cmd.ExecuteScalar() ?? 0L);
        }
    }

    /// <summary>(Re)build the catalog from entries (replaces any existing content).</summary>
    public void Build(IEnumerable<CatalogEntry> entries)
    {
        using var connection = Open();
        using var tx = connection.BeginTransaction();

        using (var clear = connection.CreateCommand())
        {
            clear.Transaction = tx;
            clear.CommandText = "DELETE FROM Telltales; DELETE FROM CatalogEntries;";
            clear.ExecuteNonQuery();
        }

        foreach (var entry in entries)
        {
            using (var ins = connection.CreateCommand())
            {
                ins.Transaction = tx;
                ins.CommandText =
                    "INSERT INTO CatalogEntries (Id, Title, ProfileJson) VALUES ($id, $title, $json);";
                ins.Parameters.AddWithValue("$id", entry.Id);
                ins.Parameters.AddWithValue("$title", entry.Title);
                ins.Parameters.AddWithValue("$json", JsonSerializer.Serialize(entry.Profile, JsonOptions));
                ins.ExecuteNonQuery();
            }

            foreach (var telltale in entry.Telltales)
            {
                using var tt = connection.CreateCommand();
                tt.Transaction = tx;
                tt.CommandText = "INSERT INTO Telltales (EntryId, FileName) VALUES ($id, $file);";
                tt.Parameters.AddWithValue("$id", entry.Id);
                tt.Parameters.AddWithValue("$file", Normalize(telltale));
                tt.ExecuteNonQuery();
            }
        }

        tx.Commit();
    }

    /// <summary>Best curated profile for a set of content filenames, or null if none matches.</summary>
    public GameProfile? Match(IEnumerable<string> contentFileNames)
    {
        var names = contentFileNames
            .Select(Normalize)
            .Where(n => n.Length > 0)
            .Distinct()
            .ToList();
        if (names.Count == 0)
            return null;

        using var connection = Open();
        using (var temp = connection.CreateCommand())
        {
            // IF NOT EXISTS + clear: pooled connections may carry the temp table over.
            temp.CommandText =
                "CREATE TEMP TABLE IF NOT EXISTS content (FileName TEXT PRIMARY KEY); DELETE FROM content;";
            temp.ExecuteNonQuery();
        }

        using (var tx = connection.BeginTransaction())
        {
            using var ins = connection.CreateCommand();
            ins.Transaction = tx;
            ins.CommandText = "INSERT OR IGNORE INTO content (FileName) VALUES ($f);";
            var p = ins.Parameters.Add("$f", SqliteType.Text);
            foreach (var name in names)
            {
                p.Value = name;
                ins.ExecuteNonQuery();
            }

            tx.Commit();
        }

        // Entries whose every telltale is present, most specific first.
        string? entryId;
        using (var q = connection.CreateCommand())
        {
            q.CommandText = """
                SELECT t.EntryId
                FROM Telltales t
                LEFT JOIN content c ON c.FileName = t.FileName
                GROUP BY t.EntryId
                HAVING COUNT(*) = SUM(CASE WHEN c.FileName IS NOT NULL THEN 1 ELSE 0 END)
                ORDER BY COUNT(*) DESC
                LIMIT 1;
                """;
            entryId = q.ExecuteScalar() as string;
        }

        return entryId is null ? null : LoadProfile(connection, entryId);
    }

    private static GameProfile? LoadProfile(SqliteConnection connection, string entryId)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT ProfileJson FROM CatalogEntries WHERE Id = $id;";
        cmd.Parameters.AddWithValue("$id", entryId);
        return cmd.ExecuteScalar() is string json
            ? JsonSerializer.Deserialize<GameProfile>(json, JsonOptions)
            : null;
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys = ON;";
        pragma.ExecuteNonQuery();
        return connection;
    }

    private static void Initialize(SqliteConnection connection)
    {
        using var versionCmd = connection.CreateCommand();
        versionCmd.CommandText = "PRAGMA user_version;";
        if ((long)(versionCmd.ExecuteScalar() ?? 0L) >= 1)
            return;

        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE CatalogEntries (
                Id          TEXT PRIMARY KEY,
                Title       TEXT NOT NULL,
                ProfileJson TEXT NOT NULL
            );

            CREATE TABLE Telltales (
                EntryId  TEXT NOT NULL,
                FileName TEXT NOT NULL,
                FOREIGN KEY(EntryId) REFERENCES CatalogEntries(Id) ON DELETE CASCADE
            );

            CREATE INDEX idx_telltale_file ON Telltales(FileName);

            PRAGMA user_version = 1;
            """;
        cmd.ExecuteNonQuery();
    }

    private static string Normalize(string fileName) =>
        Path.GetFileName(fileName).Trim().ToLowerInvariant();
}
