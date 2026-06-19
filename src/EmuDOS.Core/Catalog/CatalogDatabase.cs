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
                    "INSERT OR REPLACE INTO CatalogEntries (Id, Title, NormalizedTitle, NormalizedPrimary, ProfileJson) VALUES ($id, $title, $ntitle, $nprimary, $json);";
                ins.Parameters.AddWithValue("$id", entry.Id);
                ins.Parameters.AddWithValue("$title", entry.Title);
                ins.Parameters.AddWithValue("$ntitle", NormalizeTitle(entry.Title));
                ins.Parameters.AddWithValue("$nprimary", NormalizeTitle(PrimaryTitle(entry.Title)));
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

    /// <summary>
    /// Curated profile for a game title (normalized), or null. The fallback when content/telltale
    /// matching can't identify the game — eXoDOS launches most games via a run.bat we can't see,
    /// so the title is the reliable key.
    /// </summary>
    public GameProfile? MatchByTitle(string title)
    {
        var full = NormalizeTitle(title);
        if (full.Length == 0)
            return null;
        var primary = NormalizeTitle(PrimaryTitle(title));

        using var connection = Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT ProfileJson FROM CatalogEntries
            WHERE NormalizedTitle = $full OR NormalizedPrimary = $full
               OR ($primary <> '' AND (NormalizedTitle = $primary OR NormalizedPrimary = $primary))
            ORDER BY CASE
                WHEN NormalizedTitle = $full THEN 0
                WHEN NormalizedPrimary = $full THEN 1
                WHEN NormalizedTitle = $primary THEN 2
                ELSE 3 END
            LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$full", full);
        cmd.Parameters.AddWithValue("$primary", primary);
        return cmd.ExecuteScalar() is string json
            ? JsonSerializer.Deserialize<GameProfile>(json, JsonOptions)
            : null;
    }

    /// <summary>
    /// Collapse a title to a comparison key: lowercase, drop year/articles/punctuation, and turn
    /// multi-letter roman numerals into digits so "DOOM II" and "Doom 2" land on the same key.
    /// </summary>
    public static string NormalizeTitle(string title)
    {
        var s = (title ?? string.Empty).ToLowerInvariant();
        s = YearRegex.Replace(s, " ");
        s = RomanRegex.Replace(s, m => RomanToArabic(m.Value));
        s = ArticleRegex.Replace(s, " ");
        s = NoiseWordRegex.Replace(s, " "); // "episode 1" -> "1", etc.
        return NonAlnumRegex.Replace(s, string.Empty);
    }

    /// <summary>The part of a title before its subtitle (" - " or ": "), for looser matching.</summary>
    public static string PrimaryTitle(string title)
    {
        if (string.IsNullOrEmpty(title))
            return title ?? string.Empty;
        int dash = title.IndexOf(" - ", StringComparison.Ordinal);
        int colon = title.IndexOf(": ", StringComparison.Ordinal);
        int cut = dash >= 0 && (colon < 0 || dash < colon) ? dash : colon;
        return cut > 0 ? title[..cut] : title;
    }

    private static string RomanToArabic(string roman) => roman switch
    {
        "ii" => "2", "iii" => "3", "iv" => "4", "vi" => "6", "vii" => "7",
        "viii" => "8", "ix" => "9", "xi" => "11", "xii" => "12", "xiii" => "13",
        _ => roman,
    };

    private static readonly System.Text.RegularExpressions.Regex YearRegex =
        new(@"\(?\b(19|20)\d{2}\b\)?", System.Text.RegularExpressions.RegexOptions.Compiled);

    // Multi-letter romans only — single i/v/x are too ambiguous (e.g. "Mega Man X").
    private static readonly System.Text.RegularExpressions.Regex RomanRegex =
        new(@"\b(xiii|xii|xi|viii|vii|vi|ix|iv|iii|ii)\b", System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly System.Text.RegularExpressions.Regex ArticleRegex =
        new(@"\b(the|a|an)\b", System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly System.Text.RegularExpressions.Regex NoiseWordRegex =
        new(@"\b(episode|ep|part|chapter|volume|vol|disk|disc)\b", System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly System.Text.RegularExpressions.Regex NonAlnumRegex =
        new(@"[^a-z0-9]+", System.Text.RegularExpressions.RegexOptions.Compiled);

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
                Id                TEXT PRIMARY KEY,
                Title             TEXT NOT NULL,
                NormalizedTitle   TEXT NOT NULL,
                NormalizedPrimary TEXT NOT NULL,
                ProfileJson       TEXT NOT NULL
            );

            CREATE TABLE Telltales (
                EntryId  TEXT NOT NULL,
                FileName TEXT NOT NULL,
                FOREIGN KEY(EntryId) REFERENCES CatalogEntries(Id) ON DELETE CASCADE
            );

            CREATE INDEX idx_telltale_file ON Telltales(FileName);
            CREATE INDEX idx_catalog_title ON CatalogEntries(NormalizedTitle);
            CREATE INDEX idx_catalog_primary ON CatalogEntries(NormalizedPrimary);

            PRAGMA user_version = 1;
            """;
        cmd.ExecuteNonQuery();
    }

    // Match by executable BASENAME (no extension): eXoDOS configs only know the exe name
    // (e.g. "bonde"), not whether the user's copy has BONDE.EXE or BONDE.COM.
    private static string Normalize(string fileName) =>
        Path.GetFileNameWithoutExtension(fileName).Trim().ToLowerInvariant();
}
