using Geminarr.Core.Configuration;
using Microsoft.Extensions.Options;

namespace Geminarr.Core.Browser;

/// <summary>The desktop's public address is an origin of its own (ADR 0019): the same address as the page's would run the desktop's scripts where the operator's cookie is sent, which is what the separate origin is for. The start is refused.</summary>
public sealed class DesktopOriginValidator(IOptions<GeminarrOptions> app) : IValidateOptions<BrowserOptions>
{
    public ValidateOptionsResult Validate(string? name, BrowserOptions options)
    {
        // The address that a person is given is the one that DesktopAddress builds (the public address when set, else the page's host on the desktop's port): that is the one that must not be the page's.
        var same = Uri.TryCreate(app.Value.PublicUrl, UriKind.Absolute, out var page)
            && Uri.TryCreate(DesktopAddress.Origin(options, app.Value.PublicUrl, page.Scheme, page.Host), UriKind.Absolute, out var desktop)
            && desktop.Scheme == page.Scheme && string.Equals(desktop.Host, page.Host, StringComparison.OrdinalIgnoreCase) && desktop.Port == page.Port;
        return same
            ? ValidateOptionsResult.Fail("Browser:DesktopPublicUrl (GEMINARR__BROWSER__DESKTOPPUBLICURL) is, or leads to, the address of the page (GEMINARR__PUBLICURL): the desktop is another program's and needs an origin of its own, another host or another port.")
            : ValidateOptionsResult.Success;
    }
}
