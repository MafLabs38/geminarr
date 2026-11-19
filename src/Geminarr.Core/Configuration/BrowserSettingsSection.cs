using Geminarr.Core.Browser;
using Microsoft.Extensions.Options;

namespace Geminarr.Core.Configuration;

/// <summary>The settings of the browser container's desktop, for the page of the operator.</summary>
public sealed class BrowserSettingsSection : ISettingsSection
{
    private const string NotSet = "not set";

    private readonly IOptions<BrowserOptions> _browser;

    public BrowserSettingsSection(IOptions<BrowserOptions> browser)
    {
        _browser = browser;
    }

    public IReadOnlyList<SettingValue> Read() =>
    [
        new("GEMINARR__BROWSER__DESKTOPURL", string.IsNullOrWhiteSpace(_browser.Value.DesktopUrl) ? NotSet : "set", false),
        new("GEMINARR__BROWSER__DESKTOPPORT", _browser.Value.DesktopPort.ToString(System.Globalization.CultureInfo.InvariantCulture), false),
        new("GEMINARR__BROWSER__DESKTOPPUBLICURL", string.IsNullOrWhiteSpace(_browser.Value.DesktopPublicUrl) ? NotSet : _browser.Value.DesktopPublicUrl, false),
        new("GEMINARR__BROWSER__ALLOWEDOPENPORTS", string.IsNullOrWhiteSpace(_browser.Value.AllowedOpenPorts) ? NotSet : _browser.Value.AllowedOpenPorts, false),
        new("GEMINARR__BROWSER__REQUIREEGRESSGUARD", _browser.Value.RequireEgressGuard ? "true" : "false", false),
    ];
}
