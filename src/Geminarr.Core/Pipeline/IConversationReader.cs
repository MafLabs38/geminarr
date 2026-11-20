namespace Geminarr.Core.Pipeline;

/// <summary>Reads a web assistant (SPEC §6.1). The web reader is the real one; a demonstration one serves a sandbox and the tests.</summary>
public interface IConversationReader
{
    /// <summary>Where this reader's conversations come from (<c>web</c>): the source of the meta line that goes to Mnemarr.</summary>
    string Source { get; }

    /// <summary>
    /// The address of the account that this reader reads as while somebody is signed in, or null (nobody is signed in, or the reader has no account). It is kept in memory for the page and told apart by its hash (SPEC §8.1).
    /// </summary>
    string? AccountAddress => null;

    /// <summary>Whether a session is signed in to an account that the reader cannot name (the page did not say which): it is not the account that was signed in last, and nothing is read for it.</summary>
    bool SignedInWithoutAccount => false;

    Task<SessionCheck> CheckSessionAsync(CancellationToken cancellationToken);

    /// <summary>The most recent conversations, most recent first, at most <paramref name="limit"/>.</summary>
    Task<IReadOnlyList<ConversationHeader>> ListRecentAsync(int limit, CancellationToken cancellationToken);

    /// <summary>The turns of a conversation: all of them, or its end only when the reader knew what was already held (see <see cref="ConversationContent"/>).</summary>
    Task<ConversationContent> OpenAsync(string id, CancellationToken cancellationToken);
}
