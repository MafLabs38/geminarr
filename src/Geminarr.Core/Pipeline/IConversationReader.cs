namespace Geminarr.Core.Pipeline;

/// <summary>Reads a web assistant (SPEC §6.1). Two implementations are meant (the web reader, the Takeout importer); a demonstration one serves a sandbox and the tests.</summary>
public interface IConversationReader
{
    Task<SessionCheck> CheckSessionAsync(CancellationToken cancellationToken);

    /// <summary>The most recent conversations, most recent first, at most <paramref name="limit"/>.</summary>
    Task<IReadOnlyList<ConversationHeader>> ListRecentAsync(int limit, CancellationToken cancellationToken);

    Task<IReadOnlyList<Turn>> OpenAsync(string id, CancellationToken cancellationToken);
}
