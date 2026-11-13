using Geminarr.Core.Browser;
using Geminarr.Core.Connection;

namespace Geminarr.Core.Pipeline;

/// <summary>
/// The reader of the assistant's web page. In this version it knows the session (does the profile hold one, and is it still good) and nothing else: the conversations are read by the next
/// vertical, once the first spike has said how the page is read. It lists none, so a pass keeps the connection alive and says so.
/// </summary>
public sealed class WebReader : IConversationReader
{
    private readonly IBrowserHost _browser;

    public WebReader(IBrowserHost browser)
    {
        _browser = browser;
    }

    public string Source => ConversationSources.Web;

    public Task<SessionCheck> CheckSessionAsync(CancellationToken cancellationToken) =>
        _browser.Profile().HasSession ? _browser.CheckSessionAsync(cancellationToken) : Task.FromResult(SessionCheck.NoProfile);

    public Task<IReadOnlyList<ConversationHeader>> ListRecentAsync(int limit, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ConversationHeader>>([]);

    public Task<IReadOnlyList<Turn>> OpenAsync(string id, CancellationToken cancellationToken) =>
        throw new ReaderException(ConnectionReasons.ReaderBroken, "This version does not read conversations yet.");
}
