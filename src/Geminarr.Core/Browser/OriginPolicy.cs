namespace Geminarr.Core.Browser;

/// <summary>
/// Which origins the browser may load (D-08, SPEC §7.3): the assistant's own, the hosts its page needs, and the account provider's sign-in pages <b>only during a live view</b>. Everything else
/// is refused, so that a page that is not the assistant's cannot be reached from the signed-in profile. A pure function, tested without a browser.
/// </summary>
public static class OriginPolicy
{
    /// <summary>The host of the assistant, or null when the address is not one that may be used.</summary>
    public static string? AssistantHost(BrowserOptions options) =>
        Uri.TryCreate(options.AssistantUrl, UriKind.Absolute, out var uri) && IsAllowedScheme(uri, options) && uri.Host.Length > 0 && uri.UserInfo.Length == 0 ? uri.Host.ToLowerInvariant() : null;

    /// <summary>
    /// Whether the browser may load this address, now. A page that is navigated to (the browser's own address, a frame) is the assistant's, or in a live view a sign-in page: the other hosts serve what the
    /// assistant's page loads, and a link to one of them is never followed (some of them serve what anybody uploaded).
    /// </summary>
    public static bool Allows(string address, BrowserOptions options, bool liveView, bool navigation = false)
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri))
        {
            return false;
        }

        // A data: or blob: address is the page's own (an image, a worker); it reaches no host.
        if (uri.Scheme is "data" or "blob" or "about")
        {
            return true;
        }

        var host = uri.Host.ToLowerInvariant();
        if (host == AssistantHost(options))
        {
            return IsAllowedScheme(uri, options);
        }

        // Plain http is for a sandbox or a test that says so, never by default.
        if (uri.Scheme != Uri.UriSchemeHttps && !(options.AllowInsecureAssistant && uri.Scheme == Uri.UriSchemeHttp))
        {
            return false;
        }

        return (!navigation && Matches(host, options.AllowedHosts)) || (liveView && Matches(host, options.SignInHosts));
    }

    /// <summary>Whether a WebSocket may be opened to this address: the same rule as for a subresource, with its own scheme read as the one of a page (<c>wss</c> is <c>https</c>).</summary>
    public static bool AllowsWebSocket(string address, BrowserOptions options, bool liveView)
    {
        var page = address;
        if (address.StartsWith("wss:", StringComparison.OrdinalIgnoreCase))
        {
            page = "https:" + address[4..];
        }
        else if (address.StartsWith("ws:", StringComparison.OrdinalIgnoreCase))
        {
            page = "http:" + address[3..];
        }

        return Allows(page, options, liveView, navigation: false);
    }

    /// <summary>How a page that was loaded is read: the assistant's own page (signed in), a sign-in page (signed out), or neither (the network, a page in between: nothing is said).</summary>
    public static PageKind Classify(string address, BrowserOptions options)
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri))
        {
            return PageKind.Other;
        }

        var host = uri.Host.ToLowerInvariant();
        if (Matches(host, options.SignInHosts))
        {
            return PageKind.SignIn;
        }

        return host == AssistantHost(options) && IsAllowedScheme(uri, options) ? PageKind.Assistant : PageKind.Other;
    }

    private static bool IsAllowedScheme(Uri uri, BrowserOptions options) => uri.Scheme == Uri.UriSchemeHttps || (options.AllowInsecureAssistant && uri.Scheme == Uri.UriSchemeHttp);

    private static bool Matches(string host, string list) =>
        list.Split([';', ',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Any(entry =>
            entry.StartsWith("*.", StringComparison.Ordinal)
                ? host.EndsWith(entry[1..], StringComparison.OrdinalIgnoreCase) && host.Length > entry.Length - 1
                : string.Equals(host, entry, StringComparison.OrdinalIgnoreCase));
}

/// <summary>What a page that the browser ended on is.</summary>
public enum PageKind
{
    /// <summary>Neither the assistant nor a sign-in page.</summary>
    Other,

    /// <summary>The assistant's own page: the session is good.</summary>
    Assistant,

    /// <summary>A sign-in page of the account provider: the session is gone, or not made yet.</summary>
    SignIn,
}
