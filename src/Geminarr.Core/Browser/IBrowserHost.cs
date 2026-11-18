using Geminarr.Core.Pipeline;

namespace Geminarr.Core.Browser;

/// <summary>What is known of the browser from outside: whether one is configured, and whether the last attachment reached it (null until one was tried).</summary>
public sealed record BrowserState(bool Configured, bool? Reachable, AccountIdentity? Account = null, bool? TabsAnswer = null);

/// <summary>
/// The browser that holds the session (ADR 0016): a container of its own, that a person signs in to by hand. Geminarr has no debugging port to it (ADR 0021): the host only looks at the web desktop, and what the session is
/// (signed in or not, as whom, whether a person is signing in) is the word of the extension of the browser. Until the extension reader says it, the answers below are "not known": no account, no session check,
/// no sign-in to watch, and a session that cannot be cleared. Everything that decides is in Core.
/// </summary>
public interface IBrowserHost
{
    /// <summary>What the last attachment found, without trying one, who the account was that it found signed in (null until a check found one, and after a sign-out), and whether the last look at the list of tabs was answered (the watcher's own, so that it does not hide what the passes found).</summary>
    BrowserState State();

    /// <summary>
    /// Attaches to the browser, loads the assistant's page in a page of its own and says whether it ended on the page or on a sign-in page, then lets go. A browser that cannot be reached is a
    /// <see cref="ReaderException"/>, never "signed out". Without a browser configured there is nothing to check.
    /// </summary>
    Task<SessionCheck> CheckSessionAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Whether a tab of the browser is on a sign-in page of the account provider at this moment, read from the list of tabs (plain HTTP, nothing is attached). A browser that cannot be reached cannot tell: the answer is then null, which is neither "signing in" nor "not signing in".
    /// The person who is signing in is waited for: when this turns from true to false, a check is worth making at once.
    /// </summary>
    Task<bool?> IsSigningInAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Opens the sign-in page of the account provider in a tab of the browser of its own, unless a tab is on a sign-in page already: the person then signs in by hand, in the browser's own page. Nothing
    /// is attached to do it (the browser is not driven), and nothing is typed. A browser that cannot be reached is a <see cref="ReaderException"/>.
    /// </summary>
    Task OpenSignInPageAsync(CancellationToken cancellationToken);

    /// <summary>Clears the session that the browser holds (its cookies): the person signs in again by hand. Nothing else of the browser is touched.</summary>
    Task DisconnectAsync(CancellationToken cancellationToken);
}
