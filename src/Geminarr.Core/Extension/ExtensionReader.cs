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

    private const string SilentSentence = "The extension of the browser has said nothing for a while: check that the browser runs and reaches Geminarr.";

    public string Source => ConversationSources.Web;

    public Task<SessionCheck> CheckSessionAsync(CancellationToken cancellationToken)
    {
        var session = _inbox.Session;
        // The operator disconnected and nobody has signed in since the sign-out was over: whatever the page was last said to show, the session being gone is what was asked for, not a login that expired (and not a mail to Mnemarr).
        // This comes first: a browser that is off, or a page that is broken, does not turn a sign-out that waits into something else.
        if (_inbox.Disconnected)
        {
            return Task.FromResult(SessionCheck.NoBrowser);
        }

        var silentAfter = TimeSpan.FromMinutes(_options.Value.SilentAfterMinutes);
        var now = _clock.GetUtcNow();
        // The silence about the page starts at the last word, or, when there is none (a restart) or no round was asked of the extension (a pause), when the extension was first heard or last heard while the reading was paused.
        var since = new[] { session?.At, _inbox.FirstHello, _inbox.LastPausedHello }.Max();
        if (since is not { } start || now - start > silentAfter)
        {
            // The extension is there (it says so every half minute) but says nothing of the page: the assistant does not answer (a block, an outage). Restarting the browser would only visit the page again, so this is told apart from a browser that does not speak.
            if (since is not null && _inbox.LastHello is { } hello && now - hello <= silentAfter)
            {
                throw new ReaderException(ConnectionReasons.Unreachable, "The extension is there but the assistant's page has not answered for a while (a block or an outage): wait, do not restart the browser.");
            }

            throw new ReaderException(ConnectionReasons.ExtensionSilent, SilentSentence);
        }

        if (session is null)
        {
            // Heard, but nothing of the page yet: still a reader that cannot say.
            throw new ReaderException(ConnectionReasons.ExtensionSilent, SilentSentence);
        }

        // A page that is not what the extension knows is not a session that is gone: it is the reader that is broken, said loudly (SPEC §6.4).
        if (session.Broken is { } step)
        {
            throw new ReaderException(ConnectionReasons.ReaderBroken, $"The page of the assistant is not what the extension knows (step: {step}).");
        }

        return Task.FromResult(session.SignedIn ? SessionCheck.SignedIn : SessionCheck.SignedOut);
    }

    /// <summary>The conversations that the extension has read, in the order of the list: what the pass can open now. The others come with the next round of the extension.</summary>
    public Task<IReadOnlyList<ConversationHeader>> ListRecentAsync(int limit, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ConversationHeader>>([.. _inbox.List().Where(header => _inbox.Has(header.Id)).Take(limit)]);

    public Task<IReadOnlyList<Turn>> OpenAsync(string id, CancellationToken cancellationToken) =>
        Task.FromResult(_inbox.Take(id) ?? []);
}
