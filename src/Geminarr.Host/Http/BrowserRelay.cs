using System.Net;
using System.Text.RegularExpressions;
using Geminarr.Api.V1.Access;
using Geminarr.Core.Access;
using Geminarr.Core.Browser;
using Geminarr.Core.Configuration;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;
using Yarp.ReverseProxy.Forwarder;

namespace Geminarr.Host.Http;

/// <summary>
/// Relays the web desktop of the browser container under <see cref="BrowserOptions.RelayPath"/> (ADR 0017). The desktop has no sign-in of its own and no address of its own: it is the loopback of the namespace that
/// the two containers share, and the only way to it is this relay, behind the sign-in of the operator (the fallback policy closes every endpoint, this one included). The person opens it to sign in to the account by hand;
/// Geminarr neither types nor clicks in it. What the relay sends on is the request of the person, without the cookie of Geminarr's session, and what comes back has no cookie either.
/// </summary>
public static partial class BrowserRelay
{
    private const string PathPattern = BrowserOptions.RelayPath + "{**path}";
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(30);

    /// <summary>How often an open WebSocket looks at whether the session that opened it is still there.</summary>
    private static readonly TimeSpan SessionWatchInterval = TimeSpan.FromSeconds(5);

    /// <summary>Whether the path is the relay's: its pages draw the screen of another program and need other headers than Geminarr's own (see <see cref="SecurityHeaders"/>).</summary>
    public static bool IsRelayPath(PathString path) => path.StartsWithSegments(BrowserOptions.RelayPath.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);

    public static IServiceCollection AddGeminarrBrowserRelay(this IServiceCollection services)
    {
        services.AddHttpForwarder();
        services.AddSingleton(_ => new RelayClient(new HttpMessageInvoker(new SocketsHttpHandler
        {
            UseProxy = false,
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.None,
            UseCookies = false,
            ConnectTimeout = TimeSpan.FromSeconds(10),
        })));
        return services;
    }

    /// <summary>Nothing is mapped without the address of the desktop: the path is then the interface's own and answers as any unknown route.</summary>
    public static WebApplication MapGeminarrBrowserRelay(this WebApplication app)
    {
        var desktop = app.Services.GetRequiredService<IOptions<BrowserOptions>>().Value.DesktopUrl;
        if (string.IsNullOrWhiteSpace(desktop))
        {
            return app;
        }

        var destination = desktop.TrimEnd('/') + "/";
        var forwarder = app.Services.GetRequiredService<IHttpForwarder>();
        var client = app.Services.GetRequiredService<RelayClient>().Invoker;
        var request = new ForwarderRequestConfig { ActivityTimeout = IdleTimeout };
        app.Map(PathPattern, async context =>
        {
            // The desktop is served under the path with its slash: without it, the person is sent there (routing matches both, so the endpoint tells them apart).
            if (string.Equals(context.Request.Path.Value, BrowserOptions.RelayPath.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
            {
                context.Response.Redirect(BrowserOptions.RelayPath);
                return;
            }

            // A desktop that could be reached by something else than Geminarr is not relayed: the person is told why, and the health says it (browser-exposed).
            if (context.RequestServices.GetRequiredService<IBrowserExposure>().Check() is { IsExposed: true } exposure)
            {
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                await context.Response.WriteAsJsonAsync(
                    new Microsoft.AspNetCore.Mvc.ProblemDetails { Status = StatusCodes.Status503ServiceUnavailable, Title = exposure.Describe() },
                    options: null,
                    contentType: "application/problem+json",
                    cancellationToken: context.RequestAborted);
                return;
            }

            // The desktop's client reads its pages and opens one WebSocket: no write goes over HTTP, and what else the image's web server offers (files, other services) is not relayed.
            if (!IsRelayed(context.Request))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            if (context.WebSockets.IsWebSocketRequest && IsFromAnotherOrigin(context.Request.Headers.Origin.ToString(), context.Request.Host, context.RequestServices.GetRequiredService<AllowedHostList>(), context.RequestServices.GetRequiredService<IOptions<GeminarrOptions>>().Value.PublicUrl))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }

            // A WebSocket outlives the check that let it in: it ends with the session that opened it (signing out, the end of its lifetime), and does not go on for as long as it is used.
            using var stop = new CancellationTokenSource();
            var watching = context.WebSockets.IsWebSocketRequest
                ? EndWithTheSessionAsync(context, context.RequestServices.GetRequiredService<SessionRegistry>(), context.RequestServices.GetRequiredService<IBrowserExposure>(), context.RequestServices.GetRequiredService<TimeProvider>(), context.User.FindFirst(AccessConstants.SessionClaim)?.Value, stop.Token)
                : Task.CompletedTask;
            try
            {
                var error = await forwarder.SendAsync(context, destination, client, request, RelayTransformer.Instance);
                if (error != ForwarderError.None && !context.Response.HasStarted)
                {
                    context.Response.StatusCode = StatusCodes.Status502BadGateway;
                }
            }
            finally
            {
                await stop.CancelAsync();
                await watching;
            }
        });
        return app;
    }

    /// <summary>
    /// What the relay forwards (ADR 0017), by a list of what the client of the pinned image asks for, and nothing else: its page, its scripts, styles, fonts and pictures under <c>assets/</c>, a few files at the root, its
    /// WebSocket and its status. Not a write, and not what else the image's web server may offer (a listing of files, other services, a development route): a page of the desktop is served on Geminarr's own origin,
    /// so a route that nobody listed is not relayed, whatever a later version of the image adds (the desktop then shows a blank page, which is visible; a route relayed by default would be silent).
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

    [GeneratedRegex(RelayedPattern, RegexOptions.CultureInvariant)]
    private static partial Regex RelayedPaths();

    /// <summary>
    /// Whether the origin that a WebSocket handshake names is another than the page's: a handshake is not bound by the same-origin policy, and the cookie of the session is only <c>SameSite</c>, which another port
    /// of the same host (a hostile page) would not stop. A request that names no origin is not from a page. The origin is the page's when it is the public address (<paramref name="publicUrl"/>: a proxy on a port of its
    /// own, or one that sends no port in its Host header), or the host and the port of the request (a direct access: the scheme cannot be told behind a proxy that ends TLS); or, when a proxy sends a Host of its own,
    /// one of the names the page may be reached by, on a default port, and then only if the list of names says something (not <c>*</c>).
    /// </summary>
    public static bool IsFromAnotherOrigin(string? origin, HostString host, AllowedHostList hosts, string? publicUrl)
    {
        if (string.IsNullOrEmpty(origin))
        {
            return false;
        }

        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
        {
            return true;
        }

        if (Uri.TryCreate(publicUrl, UriKind.Absolute, out var page) && string.Equals(page.Host, uri.Host, StringComparison.OrdinalIgnoreCase) && page.Port == uri.Port)
        {
            return false;
        }

        if (string.Equals(uri.Host, host.Host, StringComparison.OrdinalIgnoreCase))
        {
            return host.Port is { } port ? uri.Port != port : !uri.IsDefaultPort;
        }

        return hosts.AllowsAny || !hosts.IsAllowed(uri.Authority) || !uri.IsDefaultPort;
    }

    private static async Task EndWithTheSessionAsync(HttpContext context, SessionRegistry sessions, IBrowserExposure exposure, TimeProvider clock, string? sessionId, CancellationToken stop)
    {
        using var timer = new PeriodicTimer(SessionWatchInterval, clock);
        try
        {
            while (await timer.WaitForNextTickAsync(stop))
            {
                // An open socket ends with the session that opened it, and with the desktop being closed: a namespace that became exposed after the socket was opened.
                if (!sessions.IsActive(sessionId) || exposure.Check().IsExposed)
                {
                    context.Abort();
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // The WebSocket ended by itself.
        }
    }

    /// <summary>The one client of the relay, in a type of its own so that no other service takes it for theirs.</summary>
    public sealed record RelayClient(HttpMessageInvoker Invoker);

    private sealed class RelayTransformer : HttpTransformer
    {
        public static readonly RelayTransformer Instance = new();

        public override async ValueTask TransformRequestAsync(HttpContext httpContext, HttpRequestMessage proxyRequest, string destinationPrefix, CancellationToken cancellationToken)
        {
            await base.TransformRequestAsync(httpContext, proxyRequest, destinationPrefix, cancellationToken);
            // Geminarr's session is Geminarr's: the desktop never sees it.
            proxyRequest.Headers.Remove("Cookie");
            proxyRequest.Headers.Remove("Authorization");
            // The desktop is told that it is Geminarr that speaks (its own list of allowed origins names this one and no web page can ever have it): a page that the browser itself loads can reach the desktop on the
            // loopback, and must not be taken for the person's.
            proxyRequest.Headers.Remove("Origin");
            proxyRequest.Headers.TryAddWithoutValidation("Origin", BrowserOptions.RelayOrigin);
            // The desktop's web server accepts the loopback name only (the init script of the image): the name that the person reached Geminarr by is not given to it, it is reached by its own address.
            proxyRequest.Headers.Host = null;
        }

        public override async ValueTask<bool> TransformResponseAsync(HttpContext httpContext, HttpResponseMessage? proxyResponse, CancellationToken cancellationToken)
        {
            var proceed = await base.TransformResponseAsync(httpContext, proxyResponse, cancellationToken);
            httpContext.Response.Headers.Remove("Set-Cookie");
            // A service worker that the desktop registers must not reach beyond its own path, whatever the desktop says: Geminarr's own pages are not its to control.
            httpContext.Response.Headers.Remove("Service-Worker-Allowed");
            return proceed;
        }
    }
}
