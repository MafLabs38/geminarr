namespace Geminarr.Core.Browser;

/// <summary>
/// What page the browser ended on: the assistant's own (signed in), or a sign-in page of the account provider (signed out) (SPEC §7.2, D-08). Geminarr navigates to the assistant's host only, and reads
/// the page it lands on with this. A pure function, tested without a browser.
/// </summary>
public static class OriginPolicy
{
    /// <summary>The host of the assistant, or null when the address is not one that may be used.</summary>
    public static string? AssistantHost(BrowserOptions options) =>
        Uri.TryCreate(options.AssistantUrl, UriKind.Absolute, out var uri) && IsAllowedScheme(uri, options) && uri.Host.Length > 0 && uri.UserInfo.Length == 0 ? uri.Host.ToLowerInvariant() : null;

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

    /// <summary>The entries of a list of hosts, as the settings write them.</summary>
    public static string[] Entries(string list) => list.Split([';', ',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static bool Matches(string host, string list) =>
        Entries(list).Any(entry =>
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
