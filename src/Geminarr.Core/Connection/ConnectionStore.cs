using Geminarr.Core.Storage;

namespace Geminarr.Core.Connection;

public sealed class ConnectionStore : IConnectionStore
{
    private readonly IDatabase _database;
    private readonly TimeProvider _clock;

    public ConnectionStore(IDatabase database, TimeProvider clock)
    {
        _database = database;
        _clock = clock;
    }

    public ConnectionRecord Get()
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT state, since, last_ok_at, consecutive_failures, last_reason, paused_until, before_state, before_reason, before_failures FROM connection WHERE id = 1;";
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return new ConnectionRecord(ConnectionState.NotConnected, _clock.GetUtcNow(), null, 0, ConnectionReasons.NotConnected);
        }

        return new ConnectionRecord(Enum.Parse<ConnectionState>(reader.GetString(0)), StorageText.ReadMoment(reader, 1), StorageText.ReadNullableMoment(reader, 2), reader.GetInt32(3), StorageText.ReadNullableString(reader, 4), StorageText.ReadNullableMoment(reader, 5), ReadBefore(reader));
    }

    private static ConnectionBefore? ReadBefore(Microsoft.Data.Sqlite.SqliteDataReader reader)
    {
        if (reader.IsDBNull(6))
        {
            return null;
        }

        var failures = reader.IsDBNull(8) ? 0 : reader.GetInt32(8);
        return new ConnectionBefore(Enum.Parse<ConnectionState>(reader.GetString(6)), StorageText.ReadNullableString(reader, 7), failures);
    }

    public void Save(ConnectionRecord record)
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO connection (id, state, since, last_ok_at, consecutive_failures, last_reason, paused_until, before_state, before_reason, before_failures)
            VALUES (1, $state, $since, $ok, $failures, $reason, $paused, $beforeState, $beforeReason, $beforeFailures)
            ON CONFLICT (id) DO UPDATE SET state = $state, since = $since, last_ok_at = $ok, consecutive_failures = $failures, last_reason = $reason, paused_until = $paused,
                before_state = $beforeState, before_reason = $beforeReason, before_failures = $beforeFailures;
            """;
        command.Parameters.AddWithValue("$state", record.State.ToString());
        command.Parameters.AddWithValue("$since", StorageText.Moment(record.Since));
        command.Parameters.AddWithValue("$ok", StorageText.Value(StorageText.Moment(record.LastOkAt)));
        command.Parameters.AddWithValue("$failures", record.ConsecutiveFailures);
        command.Parameters.AddWithValue("$reason", StorageText.Value(record.LastReason));
        command.Parameters.AddWithValue("$paused", StorageText.Value(StorageText.Moment(record.PausedUntil)));
        command.Parameters.AddWithValue("$beforeState", StorageText.Value(record.Before?.State.ToString()));
        command.Parameters.AddWithValue("$beforeReason", StorageText.Value(record.Before?.Reason));
        command.Parameters.AddWithValue("$beforeFailures", record.Before is { } before ? before.Failures : DBNull.Value);
        command.ExecuteNonQuery();
    }
}
