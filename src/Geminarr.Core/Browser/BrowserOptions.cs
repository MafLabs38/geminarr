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

    /// <summary>The page of the assistant that is opened to check the session; its host is the only one Geminarr navigates to.</summary>
    public string AssistantUrl { get; set; } = "https://gemini.google.com/app";

    /// <summary>The sign-in pages of the account provider: a redirect to one of them says that the session is gone.</summary>
    public string SignInHosts { get; set; } = "accounts.google.com";

    /// <summary>
    /// The page of the account provider that the "Sign in" button of the Connection page opens in the browser, in a tab of its own (a person types their credentials there, by hand; Geminarr never does). Its host is one of
    /// <see cref="SignInHosts"/> and it brings the person back to the assistant's page.
    /// </summary>
    public string SignInUrl { get; set; } = "https://accounts.google.com/ServiceLogin?continue=https%3A%2F%2Fgemini.google.com%2Fapp";

    /// <summary>
    /// The names (never the values) of the cookies that say that the account is signed in, separated by <c>;</c>: one of them must be in the browser. The assistant's page is shown to a visitor who is not
    /// signed in (a guest mode), so the page alone does not say it. Empty: the page alone decides.
    /// </summary>
    public string SessionCookies { get; set; } = "SID;__Secure-1PSID";

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

    /// <summary>A sandbox or a test only: the assistant and the sign-in pages may be on plain http.</summary>
    public bool AllowInsecureAssistant { get; set; }

    /// <summary>
    /// How many passes in a row step aside because a tab of the browser is on a sign-in page (a person is signing in). A tab that stays there longer is not somebody typing (an account picker left open, a prompt
    /// never finished): the pass then looks at the session as usual.
    /// </summary>
    public int SigningInPasses { get; set; } = 3;

    /// <summary>How long the page of the assistant may take to load when the session is checked.</summary>
    public int SessionCheckTimeoutSeconds { get; set; } = 30;
}

public sealed class BrowserOptionsValidator : IValidateOptions<BrowserOptions>
{
    public ValidateOptionsResult Validate(string? name, BrowserOptions options)
    {
        var problem =
            (OriginPolicy.AssistantHost(options) is null ? "Browser:AssistantUrl (GEMINARR__BROWSER__ASSISTANTURL) must be an https address with a host (http only when the sandbox switch allows it)." : null)
            ?? Address("DesktopUrl", options.DesktopUrl)
            ?? Loopback("DesktopUrl", options.DesktopUrl)
            ?? DesktopOrigin(options)
            ?? Hosts("SignInHosts", options.SignInHosts)
            ?? SignInUrl(options)
            ?? (options.SessionCheckTimeoutSeconds is >= 5 and <= 300 ? null : "Browser:SessionCheckTimeoutSeconds (GEMINARR__BROWSER__SESSIONCHECKTIMEOUTSECONDS) must be between 5 and 300.")
            ?? OpenPorts(options)
            ?? (options.SigningInPasses is >= 1 and <= 100 ? null : "Browser:SigningInPasses (GEMINARR__BROWSER__SIGNINGINPASSES) must be between 1 and 100.");
        return problem is null ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(problem);
    }

    /// <summary>A wildcard covers the names under a domain of its own (<c>*.example.org</c>): never a whole public suffix (<c>*.org</c>), and a list is not empty.</summary>
    private static string? SignInUrl(BrowserOptions options) =>
        Uri.TryCreate(options.SignInUrl, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttps || (options.AllowInsecureAssistant && uri.Scheme == Uri.UriSchemeHttp))
        && uri.UserInfo.Length == 0
        && OriginPolicy.Classify(options.SignInUrl, options) == PageKind.SignIn
            ? null
            : "Browser:SignInUrl (GEMINARR__BROWSER__SIGNINURL) must be an https address on one of the SignInHosts, without a user name (http only when the sandbox switch allows it).";

    private static string? Hosts(string setting, string list)
    {
        var entries = OriginPolicy.Entries(list);
        if (entries.Length == 0 || entries.Any(entry => entry.StartsWith("*.", StringComparison.Ordinal) && !entry[2..].Contains('.', StringComparison.Ordinal)))
        {
            return $"Browser:{setting} (GEMINARR__BROWSER__{setting.ToUpperInvariant()}) must name hosts, and a wildcard only under a domain of its own (*.example.org, not *.org).";
        }

        return null;
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
        var ports = OriginPolicy.Entries(options.AllowedOpenPorts);
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
