namespace Geminarr.Core.Pipeline;

/// <summary>Reads a web assistant (SPEC §6.1). The web reader is the real one; a demonstration one serves a sandbox and the tests.</summary>
public interface IConversationReader
{
    /// <summary>Where this reader's conversations come from (<c>web</c>): the source of the meta line that goes to Mnemarr.</summary>
    string Source { get; }

    Task<SessionCheck> CheckSessionAsync(CancellationToken cancellationToken);

    /// <summary>The most recent conversations, most recent first, at most <paramref name="limit"/>.</summary>
    Task<IReadOnlyList<ConversationHeader>> ListRecentAsync(int limit, CancellationToken cancellationToken);

    /// <summary>The turns of a conversation: all of them, or its end only when the reader knew what was already held (see <see cref="ConversationContent"/>).</summary>
    Task<ConversationContent> OpenAsync(string id, CancellationToken cancellationToken);
}
