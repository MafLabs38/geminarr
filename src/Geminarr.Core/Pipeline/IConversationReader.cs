namespace Geminarr.Core.Pipeline;

/// <summary>Reads a web assistant (SPEC §6.1). Two implementations are meant (the web reader, the Takeout importer); a demonstration one serves a sandbox and the tests.</summary>
public interface IConversationReader
{
    /// <summary>Where this reader's conversations come from (<c>web</c>, <c>takeout</c>): the source of the meta line that goes to Mnemarr.</summary>
    string Source { get; }

    Task<SessionCheck> CheckSessionAsync(CancellationToken cancellationToken);

    /// <summary>The most recent conversations, most recent first, at most <paramref name="limit"/>.</summary>
    Task<IReadOnlyList<ConversationHeader>> ListRecentAsync(int limit, CancellationToken cancellationToken);

    Task<IReadOnlyList<Turn>> OpenAsync(string id, CancellationToken cancellationToken);
}
