using System.Text.RegularExpressions;
using Geminarr.Core.Browser;
using Microsoft.AspNetCore.Http.Features;

namespace Geminarr.Host.Http;

/// <summary>What the relay of the desktop lets through and from whom (ADR 0017, 0019): the pure rules, tested without a browser.</summary>
public static partial class RelayRules
{
    /// <summary>Whether the path is the relay's: its pages draw the screen of another program and need other headers than Geminarr's own (see <see cref="SecurityHeaders"/>).</summary>
    public static bool IsRelayPath(PathString path) => path.StartsWithSegments(BrowserOptions.RelayPath.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// What the relay forwards (ADR 0017), by a list of what the client of the pinned image asks for, and nothing else: its page, its scripts, styles, fonts and pictures under <c>assets/</c>, a few files at the root, its
    /// WebSocket and its status. Not a write, and not what else the image's web server may offer (a listing of files, other services, a development route): the desktop's pages run on an origin of their own (ADR 0019), but it is still another program's
    /// web server behind them: a route that nobody listed is not relayed, whatever a later version of the image adds (the desktop then shows a blank page, which is visible; a route relayed by default would be silent).
    /// The path is matched as it came, in its plain form, because the desktop's web server merges slashes, decodes and resolves dots before it picks a route.
    /// </summary>
    public static bool IsRelayed(HttpRequest request)
    {
        if (!HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method) && !HttpMethods.IsOptions(request.Method))
        {
            return false;
        }

        var raw = request.HttpContext.Features.Get<IHttpRequestFeature>()?.RawTarget;
        var target = string.IsNullOrEmpty(raw) ? request.Path.Value ?? string.Empty : raw;
        var query = target.IndexOf('?', StringComparison.Ordinal);
        return RelayedPaths().IsMatch(query < 0 ? target : target[..query]);
    }

    private const string RelayedPattern = "^" + BrowserOptions.RelayPath + @"(|api/websockets|api/status|manifest\.json|selkies-core\.js|icon(-[0-9]+)?\.png|assets/[A-Za-z0-9._-]+\.(js|css|woff2?|png|svg|wasm|json))$";

    [GeneratedRegex(RelayedPattern, RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex RelayedPaths();

    /// <summary>
    /// Whether the origin that a WebSocket handshake names is another than the desktop's own: a handshake is not bound by the same-origin policy, and the desktop's cookie is only <c>SameSite</c>, which another port of the
    /// same host (a hostile page) would not stop. A request that names no origin is not from a page. The origin is the desktop's when it is its public address (<paramref name="desktopPublicUrl"/>: a proxy that gives it an address
    /// of its own, with or without a port in its Host header, and the same scheme), or the host and the port of the request (a direct access: the scheme cannot be told behind a proxy that ends TLS). Nothing else is:
    /// not the operator's page, which is another origin (ADR 0019).
    /// </summary>
    public static bool IsFromAnotherOrigin(string? origin, HostString host, string? desktopPublicUrl)
    {
        if (string.IsNullOrEmpty(origin))
        {
            return false;
        }

        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
        {
            return true;
        }

        if (Uri.TryCreate(desktopPublicUrl, UriKind.Absolute, out var own) && own.Scheme == uri.Scheme && string.Equals(own.Host, uri.Host, StringComparison.OrdinalIgnoreCase) && own.Port == uri.Port)
        {
            return false;
        }

        return !string.Equals(uri.Host, host.Host, StringComparison.OrdinalIgnoreCase) || (host.Port is { } port ? uri.Port != port : !uri.IsDefaultPort);
    }
}
