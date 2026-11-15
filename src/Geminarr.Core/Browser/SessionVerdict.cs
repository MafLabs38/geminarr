using Geminarr.Core.Connection;
using Geminarr.Core.Pipeline;

namespace Geminarr.Core.Browser;

/// <summary>A tab of the browser as its debugging port lists it: its kind (<c>page</c>, <c>service_worker</c>…) and its address.</summary>
public sealed record BrowserTab(string? Type, string? Url);

/// <summary>How the page the browser ended on, and what it saw on the way, say whether the session is good (SPEC §7.2). A pure function: the browser only tells it what it saw.</summary>
public static class SessionVerdict
{
    /// <summary>
    /// Signed out when the browser ended on a sign-in page (a redirect of the assistant to one), or ended on the assistant's page without the cookie of a signed-in account (a guest mode shows the page to a visitor);
    /// signed in when it ended on the assistant's own page with that cookie; any other page is the reader's problem.
    /// </summary>
    public static SessionCheck Decide(PageKind finalPage, bool sessionCookiePresent)
    {
        if (finalPage == PageKind.SignIn)
        {
            return SessionCheck.SignedOut;
        }

        if (finalPage == PageKind.Assistant)
        {
            return sessionCookiePresent ? SessionCheck.SignedIn : SessionCheck.SignedOut;
        }

        throw new ReaderException(ConnectionReasons.ReaderBroken, "The assistant's page is not what the reader knows.");
    }

    /// <summary>Whether a person is signing in at this moment: a page of the browser is on a sign-in page of the account provider. Nothing is attached to know it, so a pass can step aside.</summary>
    public static bool APersonIsSigningIn(IEnumerable<BrowserTab> tabs, BrowserOptions options) =>
        tabs.Any(tab => tab.Type == "page" && tab.Url is not null && OriginPolicy.Classify(tab.Url, options) == PageKind.SignIn);

    /// <summary>Whether the names of the cookies that the browser holds include one that says the account is signed in (an empty list of names asks nothing).</summary>
    public static bool HasSessionCookie(IEnumerable<string> cookieNames, BrowserOptions options)
    {
        var wanted = OriginPolicy.Entries(options.SessionCookies);
        return wanted.Length == 0 || cookieNames.Any(name => wanted.Contains(name, StringComparer.Ordinal));
    }
}
