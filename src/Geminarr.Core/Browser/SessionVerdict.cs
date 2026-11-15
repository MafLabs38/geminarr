using Geminarr.Core.Connection;
using Geminarr.Core.Pipeline;

namespace Geminarr.Core.Browser;

/// <summary>How the page the browser ended on, and what it saw on the way, say whether the session is good (SPEC §7.2). A pure function: the browser only tells it what it saw.</summary>
public static class SessionVerdict
{
    /// <summary>Why the assistant's page could not be loaded: a page that the policy refused is a page that is no longer what the reader knows; the network and the time are not the session's.</summary>
    public static string ReasonOfLoadFailure(bool refusedByThePolicy) => refusedByThePolicy ? ConnectionReasons.ReaderBroken : ConnectionReasons.Unreachable;

    /// <summary>
    /// Signed out when the browser was sent to a sign-in page (or ended on one), or ended on the assistant's page without the cookie of a signed-in account (a guest mode shows the page to a visitor);
    /// signed in when it ended on the assistant's own page with that cookie; any other page is the reader's problem.
    /// </summary>
    public static SessionCheck Decide(bool signInRedirectSeen, PageKind finalPage, bool sessionCookiePresent)
    {
        if (signInRedirectSeen || finalPage == PageKind.SignIn)
        {
            return SessionCheck.SignedOut;
        }

        if (finalPage == PageKind.Assistant)
        {
            return sessionCookiePresent ? SessionCheck.SignedIn : SessionCheck.SignedOut;
        }

        throw new ReaderException(ConnectionReasons.ReaderBroken, "The assistant's page is not what the reader knows.");
    }

    /// <summary>Whether the names of the cookies that the browser holds include one that says the account is signed in (an empty list of names asks nothing).</summary>
    public static bool HasSessionCookie(IEnumerable<string> cookieNames, BrowserOptions options)
    {
        var wanted = OriginPolicy.Entries(options.SessionCookies);
        return wanted.Length == 0 || cookieNames.Any(name => wanted.Contains(name, StringComparer.Ordinal));
    }
}
