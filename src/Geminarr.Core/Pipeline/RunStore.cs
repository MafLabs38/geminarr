using Geminarr.Core.Storage;
using Microsoft.Data.Sqlite;

namespace Geminarr.Core.Pipeline;

public sealed class RunStore : IRunStore
{
    private const string Columns = "id, kind, started_at, ended_at, outcome, reason, listed, opened, turns_sent, duration_ms";

    private readonly IDatabase _database;

    public RunStore(IDatabase database)
    {
        _database = database;
    }

    public long Start(string kind, DateTimeOffset at)
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO runs (kind, started_at) VALUES ($kind, $at) RETURNING id;";
        command.Parameters.AddWithValue("$kind", kind);
        command.Parameters.AddWithValue("$at", StorageText.Moment(at));
        return Convert.ToInt64(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }

    public void Finish(long id, DateTimeOffset at, RunOutcome outcome)
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE runs SET ended_at = $at, outcome = $outcome, reason = $reason, listed = $listed, opened = $opened, turns_sent = $sent, duration_ms = $ms WHERE id = $id;";
        command.Parameters.AddWithValue("$at", StorageText.Moment(at));
        command.Parameters.AddWithValue("$outcome", outcome.Outcome);
        command.Parameters.AddWithValue("$reason", StorageText.Value(outcome.Reason));
        command.Parameters.AddWithValue("$listed", outcome.Listed);
        command.Parameters.AddWithValue("$opened", outcome.Opened);
        command.Parameters.AddWithValue("$sent", outcome.TurnsSent);
        command.Parameters.AddWithValue("$ms", outcome.DurationMs);
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<RunRecord> Recent(int limit) => Select("SELECT " + Columns + " FROM runs ORDER BY id DESC LIMIT $limit;", ("$limit", limit));

    public RunRecord? Latest(string kind) => Select("SELECT " + Columns + " FROM runs WHERE kind = $kind ORDER BY id DESC LIMIT 1;", ("$kind", kind)).FirstOrDefault();

    private List<RunRecord> Select(string sql, (string Name, object Value) parameter)
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue(parameter.Name, parameter.Value);
        using var reader = command.ExecuteReader();
        var result = new List<RunRecord>();
        while (reader.Read())
        {
            result.Add(Map(reader));
        }

        return result;
    }

    private static RunRecord Map(SqliteDataReader reader) => new(
        reader.GetInt64(0),
        reader.GetString(1),
        StorageText.ReadMoment(reader, 2),
        StorageText.ReadNullableMoment(reader, 3),
        StorageText.ReadNullableString(reader, 4),
        StorageText.ReadNullableString(reader, 5),
        reader.GetInt32(6),
        reader.GetInt32(7),
        reader.GetInt32(8),
        reader.GetInt64(9));

    public void Prune(DateTimeOffset before)
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM runs WHERE started_at < $before;";
        command.Parameters.AddWithValue("$before", StorageText.Moment(before));
        command.ExecuteNonQuery();
    }
}
