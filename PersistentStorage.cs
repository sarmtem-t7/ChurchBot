using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace ChurchBot;

/// <summary>
/// SQLite-backed storage for message mappings, deduplication, and category statistics.
/// Thread-safe via a shared lock — all SQLite operations are short and non-blocking.
/// </summary>
public class PersistentStorage
{
    private readonly string _connectionString;
    private readonly object _lock = new();
    private readonly ILogger<PersistentStorage> _logger;
    private DateTime _lastCleanup = DateTime.MinValue;

    public PersistentStorage(ILogger<PersistentStorage> logger, string dbPath = "churchbot.db")
    {
        _logger = logger;
        _connectionString = $"Data Source={dbPath}";
        InitializeSchema();
    }

    private SqliteConnection OpenConnection()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        // WAL mode: better read concurrency, no reader/writer blocking
        using var pragma = conn.CreateCommand();
        pragma.CommandText = "PRAGMA journal_mode=WAL";
        pragma.ExecuteNonQuery();
        return conn;
    }

    private void InitializeSchema()
    {
        lock (_lock)
        {
            using var conn = OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS MessageMappings (
                    GroupMessageId INTEGER PRIMARY KEY,
                    UserId         INTEGER NOT NULL,
                    Category       TEXT    NOT NULL DEFAULT '',
                    CreatedAt      TEXT    NOT NULL
                );
                CREATE TABLE IF NOT EXISTS ProcessedMessages (
                    MessageId   INTEGER PRIMARY KEY,
                    ProcessedAt TEXT    NOT NULL
                );
                CREATE TABLE IF NOT EXISTS CategoryStats (
                    Category TEXT    PRIMARY KEY,
                    Count    INTEGER NOT NULL DEFAULT 0
                );
            """;
            cmd.ExecuteNonQuery();
        }
        _logger.LogInformation("Database initialized at {ConnectionString}", _connectionString);
    }

    // ── Deduplication ────────────────────────────────────────────────────────

    /// <summary>
    /// Returns true if this messageId is new (marks it as processed).
    /// Returns false if it was already seen — caller should skip it.
    /// Cleans up entries older than 24 hours once per hour.
    /// </summary>
    public bool TryMarkProcessed(int messageId)
    {
        lock (_lock)
        {
            CleanupOldProcessedIfNeeded();
            using var conn = OpenConnection();
            using var cmd = conn.CreateCommand();
            // Один запрос + ExecuteNonQuery возвращает кол-во затронутых строк:
            // 1 = новый messageId, 0 = уже обрабатывался (IGNORE)
            cmd.CommandText = "INSERT OR IGNORE INTO ProcessedMessages (MessageId, ProcessedAt) VALUES ($id, $now)";
            cmd.Parameters.AddWithValue("$id", messageId);
            cmd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O"));
            return cmd.ExecuteNonQuery() > 0;
        }
    }

    private void CleanupOldProcessedIfNeeded()
    {
        if (DateTime.UtcNow - _lastCleanup < TimeSpan.FromHours(1)) return;
        _lastCleanup = DateTime.UtcNow;

        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM ProcessedMessages WHERE ProcessedAt < $cutoff";
        cmd.Parameters.AddWithValue("$cutoff", DateTime.UtcNow.AddHours(-24).ToString("O"));
        var deleted = cmd.ExecuteNonQuery();
        if (deleted > 0)
            _logger.LogInformation("Cleaned up {Count} expired processed message IDs", deleted);
    }

    // ── Message → User mapping ───────────────────────────────────────────────

    public void MapMessage(int groupMessageId, long userId, string category)
    {
        lock (_lock)
        {
            using var conn = OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT OR REPLACE INTO MessageMappings (GroupMessageId, UserId, Category, CreatedAt)
                VALUES ($gid, $uid, $cat, $now)
            """;
            cmd.Parameters.AddWithValue("$gid", groupMessageId);
            cmd.Parameters.AddWithValue("$uid", userId);
            cmd.Parameters.AddWithValue("$cat", category);
            cmd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O"));
            cmd.ExecuteNonQuery();
        }
    }

    public long? GetUserByMessage(int groupMessageId)
    {
        lock (_lock)
        {
            using var conn = OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT UserId FROM MessageMappings WHERE GroupMessageId = $id";
            cmd.Parameters.AddWithValue("$id", groupMessageId);
            var result = cmd.ExecuteScalar();
            return result is long uid ? uid : null;
        }
    }

    // ── Statistics ───────────────────────────────────────────────────────────

    public void IncrementStat(string category)
    {
        lock (_lock)
        {
            using var conn = OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO CategoryStats (Category, Count) VALUES ($cat, 1)
                ON CONFLICT(Category) DO UPDATE SET Count = Count + 1
            """;
            cmd.Parameters.AddWithValue("$cat", category);
            cmd.ExecuteNonQuery();
        }
    }

    public Dictionary<string, long> GetStats()
    {
        lock (_lock)
        {
            var stats = new Dictionary<string, long>();
            using var conn = OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Category, Count FROM CategoryStats ORDER BY Count DESC";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                stats[reader.GetString(0)] = reader.GetInt64(1);
            return stats;
        }
    }
}
