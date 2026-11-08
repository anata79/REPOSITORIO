using Microsoft.Data.Sqlite;

namespace Monitor.Server.Data;

/// <summary>Banco SQLite num único arquivo em <c>DATA_DIR/monitor.db</c>.</summary>
public sealed class Db
{
    private readonly string _connectionString;

    public Db(string dataDir)
    {
        Directory.CreateDirectory(dataDir);
        DataDir = dataDir;
        FilePath = Path.Combine(dataDir, "monitor.db");
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = FilePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = true,
        }.ToString();
    }

    public string DataDir { get; }
    public string FilePath { get; }

    public SqliteConnection Open()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        conn.Exec("PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 5000;");
        return conn;
    }

    public void Initialize()
    {
        using var conn = Open();
        conn.Exec("PRAGMA journal_mode = WAL;");
        conn.Exec(Schema);
        Seed.Apply(conn);
    }

    private const string Schema = """
        CREATE TABLE IF NOT EXISTS users (
            id            INTEGER PRIMARY KEY,
            username      TEXT NOT NULL UNIQUE COLLATE NOCASE,
            display_name  TEXT NOT NULL,
            password_hash TEXT NOT NULL,
            created_utc   INTEGER NOT NULL
        );

        CREATE TABLE IF NOT EXISTS machines (
            id            INTEGER PRIMARY KEY,
            name          TEXT NOT NULL,
            employee      TEXT,
            token_hash    TEXT NOT NULL UNIQUE,
            active        INTEGER NOT NULL DEFAULT 1,
            created_utc   INTEGER NOT NULL,
            last_seen_utc INTEGER,
            agent_version TEXT,
            hostname      TEXT,
            windows_user  TEXT
        );

        CREATE TABLE IF NOT EXISTS segments (
            id         TEXT PRIMARY KEY,
            machine_id INTEGER NOT NULL REFERENCES machines(id) ON DELETE CASCADE,
            start_utc  INTEGER NOT NULL,
            end_utc    INTEGER NOT NULL,
            state      TEXT NOT NULL,
            personal   INTEGER NOT NULL DEFAULT 0,
            process    TEXT,
            app_name   TEXT,
            domain     TEXT,
            title      TEXT
        );
        CREATE INDEX IF NOT EXISTS ix_segments_machine_start ON segments(machine_id, start_utc);
        CREATE INDEX IF NOT EXISTS ix_segments_end ON segments(end_utc);

        CREATE TABLE IF NOT EXISTS rules (
            id       INTEGER PRIMARY KEY,
            kind     TEXT NOT NULL,
            pattern  TEXT NOT NULL COLLATE NOCASE,
            category TEXT NOT NULL,
            label    TEXT,
            UNIQUE(kind, pattern)
        );

        CREATE TABLE IF NOT EXISTS schedules (
            effective_from TEXT PRIMARY KEY,
            days_json      TEXT NOT NULL,
            created_utc    INTEGER NOT NULL,
            created_by     TEXT
        );

        CREATE TABLE IF NOT EXISTS settings (
            key   TEXT PRIMARY KEY,
            value TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS audit (
            id       INTEGER PRIMARY KEY,
            at_utc   INTEGER NOT NULL,
            username TEXT,
            action   TEXT NOT NULL,
            detail   TEXT,
            ip       TEXT
        );
        CREATE INDEX IF NOT EXISTS ix_audit_at ON audit(at_utc);
        """;
}

public static class SqliteExtensions
{
    public static int Exec(this SqliteConnection conn, string sql, params (string Name, object? Value)[] args)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        Bind(cmd, args);
        return cmd.ExecuteNonQuery();
    }

    public static int Exec(this SqliteConnection conn, SqliteTransaction tx, string sql, params (string Name, object? Value)[] args)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        Bind(cmd, args);
        return cmd.ExecuteNonQuery();
    }

    public static T? Scalar<T>(this SqliteConnection conn, string sql, params (string Name, object? Value)[] args)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        Bind(cmd, args);
        var result = cmd.ExecuteScalar();
        if (result is null || result is DBNull) return default;
        return (T)Convert.ChangeType(result, Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T));
    }

    public static List<T> Query<T>(this SqliteConnection conn, string sql, Func<SqliteDataReader, T> map, params (string Name, object? Value)[] args)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        Bind(cmd, args);
        using var reader = cmd.ExecuteReader();
        var list = new List<T>();
        while (reader.Read()) list.Add(map(reader));
        return list;
    }

    public static string? GetStringOrNull(this SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);
    public static long? GetInt64OrNull(this SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetInt64(i);

    private static void Bind(SqliteCommand cmd, (string Name, object? Value)[] args)
    {
        foreach (var (name, value) in args)
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
    }
}
