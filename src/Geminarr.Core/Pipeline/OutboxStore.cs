using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Geminarr.Core.Storage;

namespace Geminarr.Core.Pipeline;

public sealed class OutboxStore : IOutboxStore
{
    private readonly IDatabase _database;

    public OutboxStore(IDatabase database)
    {
        _database = database;
    }

    public void Replace(string conversationId, IReadOnlyList<(long From, IReadOnlyList<string> Lines)> batches, DateTimeOffset at)
    {
        using var connection = _database.Open();
        using var transaction = connection.BeginTransaction();
        using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM outbox WHERE conversation_id = $id;";
            delete.Parameters.AddWithValue("$id", conversationId);
            delete.ExecuteNonQuery();
        }

        foreach (var (from, lines) in batches)
        {
            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = "INSERT INTO outbox (conversation_id, from_position, line_count, lines, created_at) VALUES ($id, $from, $count, $lines, $at);";
            insert.Parameters.AddWithValue("$id", conversationId);
            insert.Parameters.AddWithValue("$from", from);
            insert.Parameters.AddWithValue("$count", lines.Count);
            insert.Parameters.AddWithValue("$lines", Pack(lines));
            insert.Parameters.AddWithValue("$at", StorageText.Moment(at));
            insert.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    public IReadOnlyList<OutboxBatch> Waiting(int limit, long afterId = 0)
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, conversation_id, from_position, lines, created_at, attempts, last_error FROM outbox WHERE id > $after ORDER BY id LIMIT $limit;";
        command.Parameters.AddWithValue("$limit", limit);
        command.Parameters.AddWithValue("$after", afterId);
        using var reader = command.ExecuteReader();
        var result = new List<OutboxBatch>();
        while (reader.Read())
        {
            result.Add(new OutboxBatch(reader.GetInt64(0), reader.GetString(1), reader.GetInt64(2), Unpack((byte[])reader[3]), StorageText.ReadMoment(reader, 4), reader.GetInt32(5), StorageText.ReadNullableString(reader, 6)));
        }

        return result;
    }

    public void Clear() => Run("DELETE FROM outbox;");

    public void Delete(long id) => Run("DELETE FROM outbox WHERE id = $id;", ("$id", id));

    public void RecordFailure(long id, string error) => Run("UPDATE outbox SET attempts = attempts + 1, last_error = $error WHERE id = $id;", ("$id", id), ("$error", error));

    public long PendingLines()
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COALESCE(SUM(line_count), 0) FROM outbox;";
        return Convert.ToInt64(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }

    public DateTimeOffset? OldestAt()
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT MIN(created_at) FROM outbox;";
        return command.ExecuteScalar() is string text ? DateTimeOffset.Parse(text, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind) : null;
    }

    public IReadOnlySet<string> ConversationsWaiting()
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT DISTINCT conversation_id FROM outbox;";
        using var reader = command.ExecuteReader();
        var result = new HashSet<string>(StringComparer.Ordinal);
        while (reader.Read())
        {
            result.Add(reader.GetString(0));
        }

        return result;
    }

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

    /// <summary>The lines as a JSON array of strings, compressed: a conversation's text is long and repetitive.</summary>
    private static byte[] Pack(IReadOnlyList<string> lines)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true))
        {
            gzip.Write(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(lines)));
        }

        return output.ToArray();
    }

    private static IReadOnlyList<string> Unpack(byte[] packed)
    {
        using var gzip = new GZipStream(new MemoryStream(packed), CompressionMode.Decompress);
        return JsonSerializer.Deserialize<List<string>>(gzip) ?? [];
    }
}
