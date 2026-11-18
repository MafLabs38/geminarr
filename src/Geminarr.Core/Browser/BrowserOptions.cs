using Microsoft.Extensions.Options;

namespace Geminarr.Core.Browser;

/// <summary>
/// The browser that a person signs in to and that Geminarr attaches to (ADR 0016), from the <c>Browser</c> section. The browser is a container of its own: Geminarr never launches one and never types
/// a credential.
/// </summary>
public sealed class BrowserOptions
{
    public const string SectionName = "Browser";

    /// <summary>
    /// The web desktop of the browser container (for example <c>http://127.0.0.1:3000</c>): the screen of an ordinary Chromium where a person signs in by hand. Geminarr relays it, behind its own sign-in, at
    /// <see cref="RelayPath"/>: nothing else reaches it (ADR 0017), so it is a loopback address of the namespace the two containers share. Without it, nothing is relayed.
    /// </summary>
    public string? DesktopUrl { get; set; }

    /// <summary>
    /// The port that the desktop is served on, an origin of its own (ADR 0019): the desktop is another program's, so its pages never run on the origin of Geminarr's own page, where the operator's session cookie is
    /// sent. Geminarr listens on this port too, for the desktop and nothing else. Without <see cref="DesktopUrl"/> nothing listens on it.
    /// </summary>
    public int DesktopPort { get; set; } = DefaultDesktopPort;

    /// <summary>The port of the desktop's own origin unless a setting says another.</summary>
    public const int DefaultDesktopPort = 8283;

    /// <summary>
    /// The address that the person reaches the desktop by (for example <c>https://desktop.homelab.example</c>), when a proxy gives it one: scheme, host and port, no path. By default it is the address of the page on
    /// <see cref="DesktopPort"/>.
    /// </summary>
    public string? DesktopPublicUrl { get; set; }

    /// <summary>Where Geminarr relays the web desktop of the browser; the desktop is told to serve itself under this path (<c>SUBFOLDER</c> of its image).</summary>
    public const string RelayPath = "/browser/";

    /// <summary>
    /// The origin that Geminarr gives the desktop for every request that it relays, whatever the page of the person is: the desktop is told to allow this origin and no other (<c>SELKIES_ALLOWED_ORIGINS</c> of its image).
    /// It is a name that no web page can have (the <c>.invalid</c> domain is reserved and never resolves), so a page that the browser itself loads, and that can reach the desktop on the loopback, is refused.
    /// </summary>
    public const string RelayOrigin = "https://geminarr.invalid";

    /// <summary>
    /// Ports that other containers of the same pod open on purpose, off the loopback, and that Geminarr must not take for the browser being exposed (a service-mesh proxy, a metrics exporter; the test relay of the
    /// end-to-end stack), separated by <c>;</c>. Never the web desktop's: it has no sign-in (the start is refused).
    /// </summary>
    public string AllowedOpenPorts { get; set; } = string.Empty;

    /// <summary>
    /// Whether the egress guard must be in place (ADR 0020): the default. Geminarr then tries, from time to time, a connection that the guard refuses at once, and closes the desktop and reads nothing while it is not refused (the guard fails
    /// open when it is missing: a restart that makes a new namespace, an init container left out, a user that the rules do not name). False only where the platform cannot run the guard and its rules are held by something else.
    /// </summary>
    public bool RequireEgressGuard { get; set; } = true;

}

public sealed class BrowserOptionsValidator : IValidateOptions<BrowserOptions>
{
    public ValidateOptionsResult Validate(string? name, BrowserOptions options)
    {
        var problem =
            Address("DesktopUrl", options.DesktopUrl)
            ?? Loopback("DesktopUrl", options.DesktopUrl)
            ?? DesktopOrigin(options)
            ?? OpenPorts(options);
        return problem is null ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(problem);
    }

    /// <summary>The desktop's own origin (ADR 0019): a port, and when a proxy gives it an address, an origin (scheme, host, port) and nothing after it.</summary>
    private static string? DesktopOrigin(BrowserOptions options)
    {
        if (options.DesktopPort is < 1 or > 65_535)
        {
            return "Browser:DesktopPort (GEMINARR__BROWSER__DESKTOPPORT) must be a port number.";
        }

        return Address("DesktopPublicUrl", options.DesktopPublicUrl)
            ?? (string.IsNullOrWhiteSpace(options.DesktopPublicUrl) || (Uri.TryCreate(options.DesktopPublicUrl, UriKind.Absolute, out var uri) && uri.AbsolutePath == "/" && uri.Query.Length == 0 && uri.Fragment.Length == 0)
                ? null
                : "Browser:DesktopPublicUrl (GEMINARR__BROWSER__DESKTOPPUBLICURL) must be an origin: a scheme, a host and a port, no path.");
    }

    /// <summary>The ports that are tolerated off the loopback are port numbers, and none is the browser's own: its desktop (and the TLS port that goes with it) has no sign-in.</summary>
    private static string? OpenPorts(BrowserOptions options)
    {
        var ports = SettingList.Entries(options.AllowedOpenPorts);
        if (!ports.All(port => int.TryParse(port, out var number) && number is >= 1 and <= 65535))
        {
            return "Browser:AllowedOpenPorts (GEMINARR__BROWSER__ALLOWEDOPENPORTS) must be port numbers separated by semicolons.";
        }

        var browser = new[] { options.DesktopUrl }.Where(address => Uri.TryCreate(address, UriKind.Absolute, out _)).Select(address => new Uri(address!).Port).Concat([3001]);
        return ports.Select(int.Parse).Intersect(browser).Any()
            ? "Browser:AllowedOpenPorts (GEMINARR__BROWSER__ALLOWEDOPENPORTS) may not name the port of the browser's desktop: it has no sign-in."
            : null;
    }

    /// <summary>The web desktop has no sign-in of its own (Geminarr's is the only one): it is the loopback of the namespace the two containers share, never an address of a network.</summary>
    private static string? Loopback(string setting, string? value) =>
        string.IsNullOrWhiteSpace(value) || (Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.IsLoopback)
            ? null
            : $"Browser:{setting} (GEMINARR__BROWSER__{setting.ToUpperInvariant()}) must be a loopback address (127.0.0.1, [::1] or localhost): the browser is never reached over a network.";

    private static string? Address(string setting, string? value) =>
        string.IsNullOrWhiteSpace(value) || (Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" && uri.Host.Length > 0 && uri.UserInfo.Length == 0)
            ? null
            : $"Browser:{setting} (GEMINARR__BROWSER__{setting.ToUpperInvariant()}) must be an http or https address with a host and no credentials.";
}
