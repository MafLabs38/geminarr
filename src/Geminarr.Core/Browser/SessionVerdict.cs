using Geminarr.Core.Connection;
using Geminarr.Core.Pipeline;

namespace Geminarr.Core.Browser;

/// <summary>How the page the browser ended on, and what it saw on the way, say whether the session is good (SPEC §7.2). A pure function: the browser only tells it what it saw.</summary>
public static class SessionVerdict
{
    /// <summary>Why the assistant's page could not be loaded: a page that the policy refused is a page that is no longer what the reader knows; the network and the time are not the session's.</summary>
    public static string ReasonOfLoadFailure(bool refusedByThePolicy) => refusedByThePolicy ? ConnectionReasons.ReaderBroken : ConnectionReasons.Unreachable;

    /// <summary>Signed out when the browser was sent to a sign-in page (or ended on one), signed in when it ended on the assistant's own page; any other page is the reader's problem.</summary>
    public static SessionCheck Decide(bool signInRedirectSeen, PageKind finalPage)
    {
        if (signInRedirectSeen || finalPage == PageKind.SignIn)
        {
            return SessionCheck.SignedOut;
        }

        return finalPage == PageKind.Assistant
            ? SessionCheck.SignedIn
            : throw new ReaderException(ConnectionReasons.ReaderBroken, "The assistant's page is not what the reader knows.");
    }
}
