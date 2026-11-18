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
        command.CommandText = "SELECT state, since, last_ok_at, consecutive_failures, last_reason FROM connection WHERE id = 1;";
        using var reader = command.ExecuteReader();
        return reader.Read()
            ? new ConnectionRecord(Enum.Parse<ConnectionState>(reader.GetString(0)), StorageText.ReadMoment(reader, 1), StorageText.ReadNullableMoment(reader, 2), reader.GetInt32(3), StorageText.ReadNullableString(reader, 4))
            : new ConnectionRecord(ConnectionState.NotConnected, _clock.GetUtcNow(), null, 0, ConnectionReasons.NotConnected);
    }

    public void Save(ConnectionRecord record)
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO connection (id, state, since, last_ok_at, consecutive_failures, last_reason) VALUES (1, $state, $since, $ok, $failures, $reason)
            ON CONFLICT (id) DO UPDATE SET state = $state, since = $since, last_ok_at = $ok, consecutive_failures = $failures, last_reason = $reason;
            """;
        command.Parameters.AddWithValue("$state", record.State.ToString());
        command.Parameters.AddWithValue("$since", StorageText.Moment(record.Since));
        command.Parameters.AddWithValue("$ok", StorageText.Value(StorageText.Moment(record.LastOkAt)));
        command.Parameters.AddWithValue("$failures", record.ConsecutiveFailures);
        command.Parameters.AddWithValue("$reason", StorageText.Value(record.LastReason));
        command.ExecuteNonQuery();
    }
}
