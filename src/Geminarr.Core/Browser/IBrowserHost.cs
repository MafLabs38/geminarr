using Geminarr.Core.Pipeline;

namespace Geminarr.Core.Browser;

/// <summary>What is known of the browser from outside: whether one is configured, and whether the last attachment reached it (null until one was tried).</summary>
public sealed record BrowserState(bool Configured, bool? Reachable, AccountIdentity? Account = null);

/// <summary>
/// The browser that holds the session (ADR 0016): a container of its own, that a person signs in to by hand. Geminarr attaches to it to check the session (and later to read), and can clear the session on
/// request. It never launches a browser and never types a credential. The implementation (Playwright, attached to the debugging address) lives in its own project; everything that decides is in Core.
/// </summary>
public interface IBrowserHost
{
    /// <summary>What the last attachment found, without trying one, and who the account was that it found signed in (null until a check found one, and after a sign-out).</summary>
    BrowserState State();

    /// <summary>
    /// Attaches to the browser, loads the assistant's page in a page of its own and says whether it ended on the page or on a sign-in page, then lets go. A browser that cannot be reached is a
    /// <see cref="ReaderException"/>, never "signed out". Without a browser configured there is nothing to check.
    /// </summary>
    Task<SessionCheck> CheckSessionAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Whether a tab of the browser is on a sign-in page of the account provider at this moment, read from the list of tabs (plain HTTP, nothing is attached). A browser that cannot be reached is not a person signing in.
    /// The person who is signing in is waited for: when this turns from true to false, a check is worth making at once.
    /// </summary>
    Task<bool> IsSigningInAsync(CancellationToken cancellationToken);

    /// <summary>Clears the session that the browser holds (its cookies): the person signs in again by hand. Nothing else of the browser is touched.</summary>
    Task DisconnectAsync(CancellationToken cancellationToken);
}
