using Geminarr.Core.Accounts;
using Geminarr.Core.Storage;

namespace Geminarr.Core.Connection;

public sealed class ConnectionStore : IConnectionStore
{
    private readonly IRootDatabase _database;
    private readonly TimeProvider _clock;

    public ConnectionStore(IRootDatabase database, TimeProvider clock)
    {
        _database = database;
        _clock = clock;
    }

    public ConnectionRecord Get()
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT state, since, last_ok_at, consecutive_failures, last_reason, paused_until, before_state, before_reason, before_failures, sign_out_pending FROM connection WHERE id = 1;";
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return new ConnectionRecord(ConnectionState.NotConnected, _clock.GetUtcNow(), null, 0, ConnectionReasons.NotConnected);
        }

        return new ConnectionRecord(Enum.Parse<ConnectionState>(reader.GetString(0)), StorageText.ReadMoment(reader, 1), StorageText.ReadNullableMoment(reader, 2), reader.GetInt32(3), StorageText.ReadNullableString(reader, 4), StorageText.ReadNullableMoment(reader, 5), ReadBefore(reader), reader.GetInt32(9) != 0);
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

    public string? Account()
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT account_key FROM connection WHERE id = 1;";
        return command.ExecuteScalar() is string key && AccountKey.IsValid(key) ? key : null;
    }

    public void SetAccount(string? key)
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        // The row may not exist yet (nothing was ever recorded): it is made as "not connected", which is what Get says of a missing one.
        command.CommandText = """
            INSERT INTO connection (id, state, since, account_key) VALUES (1, $state, $since, $key)
            ON CONFLICT (id) DO UPDATE SET account_key = $key;
            """;
        command.Parameters.AddWithValue("$state", ConnectionState.NotConnected.ToString());
        command.Parameters.AddWithValue("$since", StorageText.Moment(_clock.GetUtcNow()));
        command.Parameters.AddWithValue("$key", StorageText.Value(key));
        command.ExecuteNonQuery();
    }

    public void Save(ConnectionRecord record)
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO connection (id, state, since, last_ok_at, consecutive_failures, last_reason, paused_until, before_state, before_reason, before_failures, sign_out_pending)
            VALUES (1, $state, $since, $ok, $failures, $reason, $paused, $beforeState, $beforeReason, $beforeFailures, $signOutPending)
            ON CONFLICT (id) DO UPDATE SET state = $state, since = $since, last_ok_at = $ok, consecutive_failures = $failures, last_reason = $reason, paused_until = $paused,
                before_state = $beforeState, before_reason = $beforeReason, before_failures = $beforeFailures, sign_out_pending = $signOutPending;
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
        command.Parameters.AddWithValue("$signOutPending", record.SignOutPending ? 1 : 0);
        command.ExecuteNonQuery();
    }
}
