namespace Geminarr.Core.Pipeline;

public interface IConversationStore
{
    ConversationRecord? Get(string id);

    /// <summary>Every conversation that is known, most recently seen first, at most <paramref name="limit"/>.</summary>
    IReadOnlyList<ConversationRecord> Recent(int limit);

    int Count();

    void Save(ConversationRecord record);

    /// <summary>Sets what Mnemarr holds of a conversation (the truth, when it differs from what was believed).</summary>
    void SetTurnsSent(string id, int turnsSent);
}
