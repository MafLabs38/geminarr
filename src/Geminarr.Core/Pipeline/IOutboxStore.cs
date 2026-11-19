namespace Geminarr.Core.Pipeline;

/// <summary>The batches that are waiting for Mnemarr. The only place the text of a turn is held, and only until the batch is acknowledged.</summary>
public interface IOutboxStore
{
    /// <summary>Replaces what waits for a conversation by these batches (they are contiguous from where Mnemarr stands), in one step.</summary>
    void Replace(string conversationId, IReadOnlyList<(long From, IReadOnlyList<string> Lines)> batches, DateTimeOffset at);

    /// <summary>The waiting batches after the one of this id, oldest first, at most <paramref name="limit"/>: a pass reads them by pages, so that a long wait does not put every turn in memory at once.</summary>
    IReadOnlyList<OutboxBatch> Waiting(int limit, long afterId = 0);

    /// <summary>Forgets everything that waits: unlinking leaves no text of a conversation behind.</summary>
    void Clear();

    void Delete(long id);

    void RecordFailure(long id, string error);

    /// <summary>The lines still to send, for the heartbeat.</summary>
    long PendingLines();

    /// <summary>When the oldest batch that waits was made, or null when nothing waits.</summary>
    DateTimeOffset? OldestAt();

    /// <summary>The conversations that have something waiting.</summary>
    IReadOnlySet<string> ConversationsWaiting();
}
