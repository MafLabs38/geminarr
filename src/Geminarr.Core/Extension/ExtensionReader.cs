using Geminarr.Core.Connection;
using Geminarr.Core.Pipeline;
using Microsoft.Extensions.Options;

namespace Geminarr.Core.Extension;

/// <summary>
/// The reader of the web assistant (ADR 0022): it reads nothing itself. An extension in the browser that a person signed in to hands over what the page shows, and this serves it to the pass. A silent extension
/// is a reader that cannot say, never a session that is gone: only a word of the extension says that nobody is signed in.
/// </summary>
public sealed class ExtensionReader : IConversationReader
{
    private readonly ExtensionInbox _inbox;
    private readonly IOptions<ExtensionOptions> _options;
    private readonly TimeProvider _clock;

    public ExtensionReader(ExtensionInbox inbox, IOptions<ExtensionOptions> options, TimeProvider clock)
    {
        _inbox = inbox;
        _options = options;
        _clock = clock;
    }

    public string Source => ConversationSources.Web;

    public Task<SessionCheck> CheckSessionAsync(CancellationToken cancellationToken)
    {
        var session = _inbox.Session;
        // Silent is no word about the session for a while, a heartbeat or not: the browser is off, cannot reach Geminarr, or the assistant's page does not answer (a block, an outage). Nothing known yet (a restart) is a reader that cannot say.
        if (session is null || _clock.GetUtcNow() - session.At > TimeSpan.FromMinutes(_options.Value.SilentAfterMinutes))
        {
            throw new ReaderException(ConnectionReasons.ExtensionSilent, "The extension of the browser has said nothing of the assistant for a while: check that the browser runs and reaches Geminarr, and that the assistant's page answers.");
        }

        // A page that is not what the extension knows is not a session that is gone: it is the reader that is broken, said loudly (SPEC §6.4).
        if (session.Broken is { } step)
        {
            throw new ReaderException(ConnectionReasons.ReaderBroken, $"The page of the assistant is not what the extension knows (step: {step}).");
        }

        // The operator disconnected and nobody has signed in since the sign-out was over: whatever the page was last said to show, the session being gone is what was asked for, not a login that expired (and not a mail to Mnemarr).
        if (_inbox.Disconnected)
        {
            return Task.FromResult(SessionCheck.NoBrowser);
        }

        return Task.FromResult(session.SignedIn ? SessionCheck.SignedIn : SessionCheck.SignedOut);
    }

    /// <summary>The conversations that the extension has read, in the order of the list: what the pass can open now. The others come with the next round of the extension.</summary>
    public Task<IReadOnlyList<ConversationHeader>> ListRecentAsync(int limit, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ConversationHeader>>([.. _inbox.List().Where(header => _inbox.Has(header.Id)).Take(limit)]);

    public Task<IReadOnlyList<Turn>> OpenAsync(string id, CancellationToken cancellationToken) =>
        Task.FromResult(_inbox.Take(id) ?? []);
}
