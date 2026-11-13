using Geminarr.Core.Storage;

namespace Geminarr.Core.Link;

public sealed class DestinationStore : IDestinationStore
{
    private readonly IDatabase _database;

    public DestinationStore(IDatabase database)
    {
        _database = database;
    }

    public DestinationRecord? Get()
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT url, machine, linked_at, last_heartbeat_at, last_problem FROM destination WHERE id = 1;";
        using var reader = command.ExecuteReader();
        return reader.Read()
            ? new DestinationRecord(reader.GetString(0), StorageText.ReadNullableString(reader, 1), StorageText.ReadNullableMoment(reader, 2), StorageText.ReadNullableMoment(reader, 3), StorageText.ReadNullableString(reader, 4))
            : null;
    }

    public void EnsureAddress(string url) => Run("INSERT INTO destination (id, url) VALUES (1, $url) ON CONFLICT (id) DO NOTHING;", ("$url", url));

    public void SaveAddress(string url) =>
        Run("INSERT INTO destination (id, url) VALUES (1, $url) ON CONFLICT (id) DO UPDATE SET url = $url, machine = NULL, linked_at = NULL, last_heartbeat_at = NULL, last_problem = NULL;", ("$url", url));

    public void SaveLinked(string machine, DateTimeOffset at) =>
        Run("UPDATE destination SET machine = $machine, linked_at = $at, last_problem = NULL WHERE id = 1;", ("$machine", machine), ("$at", StorageText.Moment(at)));

    public void RecordHeartbeat(DateTimeOffset at, string? problem) =>
        Run("UPDATE destination SET last_heartbeat_at = $at, last_problem = $problem WHERE id = 1;", ("$at", StorageText.Moment(at)), ("$problem", StorageText.Value(problem)));

    public void Clear() => Run("DELETE FROM destination;");

    private void Run(string sql, params (string Name, object Value)[] parameters)
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        command.ExecuteNonQuery();
    }
}
