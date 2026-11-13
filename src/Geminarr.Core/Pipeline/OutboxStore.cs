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
        var wanted = batches.Select(batch => (batch.From, Packed: Pack(batch.Lines), batch.Lines.Count)).ToList();
        var kept = new HashSet<int>();
        using (var read = connection.CreateCommand())
        {
            read.Transaction = transaction;
            read.CommandText = "SELECT id, from_position, line_count, lines FROM outbox WHERE conversation_id = $id;";
            read.Parameters.AddWithValue("$id", conversationId);
            var stale = new List<long>();
            using (var reader = read.ExecuteReader())
            {
                while (reader.Read())
                {
                    // A batch that is the same as one that is wanted stays as it is, with its attempts and the moment it was made: queueing a conversation again does not forget how it went.
                    var match = wanted.FindIndex(batch => batch.From == reader.GetInt64(1) && batch.Count == reader.GetInt32(2) && batch.Packed.AsSpan().SequenceEqual((byte[])reader[3]));
                    if (match >= 0 && kept.Add(match))
                    {
                        continue;
                    }

                    stale.Add(reader.GetInt64(0));
                }
            }

            foreach (var id in stale)
            {
                using var delete = connection.CreateCommand();
                delete.Transaction = transaction;
                delete.CommandText = "DELETE FROM outbox WHERE id = $id;";
                delete.Parameters.AddWithValue("$id", id);
                delete.ExecuteNonQuery();
            }
        }

        for (var index = 0; index < wanted.Count; index++)
        {
            if (kept.Contains(index))
            {
                continue;
            }

            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = "INSERT INTO outbox (conversation_id, from_position, line_count, lines, created_at) VALUES ($id, $from, $count, $lines, $at);";
            insert.Parameters.AddWithValue("$id", conversationId);
            insert.Parameters.AddWithValue("$from", wanted[index].From);
            insert.Parameters.AddWithValue("$count", wanted[index].Count);
            insert.Parameters.AddWithValue("$lines", wanted[index].Packed);
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

    public void Clear() => RunAndScrub("DELETE FROM outbox;");

    public void Delete(long id) => RunAndScrub("DELETE FROM outbox WHERE id = $id;", ("$id", id));

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

    /// <summary>
    /// Deletes, and then empties the write-ahead log: a deleted batch is overwritten in the file (<c>secure_delete</c>) but its frames would stay in the log until it is reused, and the log is
    /// in the volume and in its backups.
    /// </summary>
    private void RunAndScrub(string sql, params (string Name, object Value)[] parameters)
    {
        Run(sql, parameters);
        Run("PRAGMA wal_checkpoint(TRUNCATE);");
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
