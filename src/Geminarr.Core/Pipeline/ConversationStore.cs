using Geminarr.Core.Storage;
using Microsoft.Data.Sqlite;

namespace Geminarr.Core.Pipeline;

public sealed class ConversationStore : IConversationStore
{
    private const string Columns = "id, title, first_seen_at, last_seen_at, last_opened_at, list_rank, turns_known, turns_sent, last_turn_hash, rewritten_turns, source, state, last_error";

    private readonly IDatabase _database;

    public ConversationStore(IDatabase database)
    {
        _database = database;
    }

    public ConversationRecord? Get(string id)
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {Columns} FROM conversations WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id);
        using var reader = command.ExecuteReader();
        return reader.Read() ? Read(reader) : null;
    }

    public IReadOnlyList<ConversationRecord> Recent(int limit)
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {Columns} FROM conversations ORDER BY last_seen_at DESC, id LIMIT $limit;";
        command.Parameters.AddWithValue("$limit", limit);
        using var reader = command.ExecuteReader();
        var result = new List<ConversationRecord>();
        while (reader.Read())
        {
            result.Add(Read(reader));
        }

        return result;
    }

    public int Count()
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM conversations;";
        return Convert.ToInt32(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }

    public void Save(ConversationRecord record)
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"""
            INSERT INTO conversations ({Columns})
            VALUES ($id, $title, $first, $last, $opened, $rank, $known, $sent, $hash, $rewritten, $source, $state, $error)
            ON CONFLICT (id) DO UPDATE SET title = $title, last_seen_at = $last, last_opened_at = $opened, list_rank = $rank, turns_known = $known, turns_sent = $sent,
                last_turn_hash = $hash, rewritten_turns = $rewritten, source = $source, state = $state, last_error = $error;
            """;
        command.Parameters.AddWithValue("$id", record.Id);
        command.Parameters.AddWithValue("$title", StorageText.Value(record.Title));
        command.Parameters.AddWithValue("$first", StorageText.Moment(record.FirstSeenAt));
        command.Parameters.AddWithValue("$last", StorageText.Moment(record.LastSeenAt));
        command.Parameters.AddWithValue("$opened", StorageText.Value(StorageText.Moment(record.LastOpenedAt)));
        command.Parameters.AddWithValue("$rank", StorageText.Value(record.ListRank));
        command.Parameters.AddWithValue("$known", record.TurnsKnown);
        command.Parameters.AddWithValue("$sent", record.TurnsSent);
        command.Parameters.AddWithValue("$hash", StorageText.Value(record.LastTurnHash));
        command.Parameters.AddWithValue("$rewritten", record.RewrittenTurns);
        command.Parameters.AddWithValue("$source", record.Source);
        command.Parameters.AddWithValue("$state", record.State);
        command.Parameters.AddWithValue("$error", StorageText.Value(record.LastError));
        command.ExecuteNonQuery();
    }

    public void SetTurnsSent(string id, int turnsSent)
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE conversations SET turns_sent = $sent WHERE id = $id;";
        command.Parameters.AddWithValue("$sent", turnsSent);
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    private static ConversationRecord Read(SqliteDataReader reader) => new(
        reader.GetString(0),
        StorageText.ReadNullableString(reader, 1),
        StorageText.ReadMoment(reader, 2),
        StorageText.ReadMoment(reader, 3),
        StorageText.ReadNullableMoment(reader, 4),
        StorageText.ReadNullableInt(reader, 5),
        reader.GetInt32(6),
        reader.GetInt32(7),
        StorageText.ReadNullableString(reader, 8),
        reader.GetInt32(9),
        reader.GetString(10),
        reader.GetString(11),
        StorageText.ReadNullableString(reader, 12));
}
