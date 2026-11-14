using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Geminarr.Core.Storage;

/// <summary>The schema, versioned with <c>PRAGMA user_version</c>: a step is applied once, in order, and never edited once released (ADR 0014).</summary>
public static class Schema
{
    private static readonly string[] Steps =
    [
        // 1: the state of the connection to the assistant, the Mnemarr that is sent to, what is known of each conversation (never the text of a turn), the batches
        // that Mnemarr has not acknowledged (the only place a turn's text is held, and only until then), and the runs.
        """
        CREATE TABLE connection (
            id INTEGER PRIMARY KEY CHECK (id = 1),
            state TEXT NOT NULL,
            since TEXT NOT NULL,
            last_ok_at TEXT,
            consecutive_failures INTEGER NOT NULL DEFAULT 0,
            last_reason TEXT
        );
        CREATE TABLE destination (
            id INTEGER PRIMARY KEY CHECK (id = 1),
            url TEXT NOT NULL,
            machine TEXT,
            linked_at TEXT,
            last_heartbeat_at TEXT,
            last_problem TEXT
        );
        CREATE TABLE conversations (
            id TEXT PRIMARY KEY,
            title_hash TEXT,
            first_seen_at TEXT NOT NULL,
            last_seen_at TEXT NOT NULL,
            last_opened_at TEXT,
            list_rank INTEGER,
            turns_known INTEGER NOT NULL DEFAULT 0,
            turns_sent INTEGER NOT NULL DEFAULT 0,
            last_turn_hash TEXT,
            rewritten_turns INTEGER NOT NULL DEFAULT 0,
            source TEXT NOT NULL,
            state TEXT NOT NULL,
            last_error TEXT
        );
        CREATE TABLE outbox (
            id INTEGER PRIMARY KEY,
            conversation_id TEXT NOT NULL,
            from_position INTEGER NOT NULL,
            line_count INTEGER NOT NULL,
            lines BLOB NOT NULL,
            created_at TEXT NOT NULL,
            attempts INTEGER NOT NULL DEFAULT 0,
            last_error TEXT
        );
        CREATE INDEX outbox_by_conversation ON outbox (conversation_id, id);
        CREATE TABLE runs (
            id INTEGER PRIMARY KEY,
            kind TEXT NOT NULL,
            started_at TEXT NOT NULL,
            ended_at TEXT,
            outcome TEXT,
            reason TEXT,
            listed INTEGER NOT NULL DEFAULT 0,
            opened INTEGER NOT NULL DEFAULT 0,
            turns_sent INTEGER NOT NULL DEFAULT 0,
            duration_ms INTEGER NOT NULL DEFAULT 0
        );
        """,
        // 2: the operator can pause the passes until a moment, and a live view or a pause remembers the state it took over.
        """
        ALTER TABLE connection ADD COLUMN paused_until TEXT;
        ALTER TABLE connection ADD COLUMN before_state TEXT;
        ALTER TABLE connection ADD COLUMN before_reason TEXT;
        ALTER TABLE connection ADD COLUMN before_failures INTEGER;
        """,
    ];

    /// <summary>The version this code writes: the number of its steps.</summary>
    public static int Version => Steps.Length;

    public static int CurrentVersion(SqliteConnection connection) => Convert.ToInt32(Scalar(connection, "PRAGMA user_version;"), CultureInfo.InvariantCulture);

    public static void Apply(SqliteConnection connection)
    {
        Execute(connection, "PRAGMA journal_mode = WAL;");
        var version = CurrentVersion(connection);
        if (version > Steps.Length)
        {
            // A rollback to an older image: its code would read and write a schema it does not know.
            throw new UnsupportedSchemaException(version, Steps.Length);
        }

        for (var step = version; step < Steps.Length; step++)
        {
            using var transaction = connection.BeginTransaction();
            Execute(connection, Steps[step], transaction);
            Execute(connection, $"PRAGMA user_version = {step + 1};", transaction);
            transaction.Commit();
        }
    }

    private static void Execute(SqliteConnection connection, string sql, SqliteTransaction? transaction = null)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static object? Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }
}
