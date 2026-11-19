namespace Geminarr.Core.Browser;

/// <summary>Where the desktop of the browser is reached (ADR 0019): the address of its own origin, which is the operator's page on another port, unless a proxy gives it an address of its own.</summary>
public static class DesktopAddress
{
    /// <summary>The origin of the desktop (scheme, host and port, no path): <see cref="BrowserOptions.DesktopPublicUrl"/> when it is set, else the page of the operator (its public address, or the one it was reached by) on the desktop's port.</summary>
    public static string Origin(BrowserOptions browser, string? publicUrl, string requestScheme, string requestHost)
    {
        if (Uri.TryCreate(browser.DesktopPublicUrl, UriKind.Absolute, out var given))
        {
            return given.GetLeftPart(UriPartial.Authority);
        }

        var scheme = requestScheme;
        var host = requestHost;
        if (Uri.TryCreate(publicUrl, UriKind.Absolute, out var page))
        {
            scheme = page.Scheme;
            host = page.Host;
        }

        return new UriBuilder(scheme, host, browser.DesktopPort).Uri.GetLeftPart(UriPartial.Authority);
    }
}
