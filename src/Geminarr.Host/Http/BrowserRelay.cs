using System.Net;
using Geminarr.Api.V1.Access;
using Geminarr.Core.Access;
using Geminarr.Core.Browser;
using Microsoft.Extensions.Options;
using Yarp.ReverseProxy.Forwarder;

namespace Geminarr.Host.Http;

/// <summary>
/// Relays the web desktop of the browser container under <see cref="BrowserOptions.RelayPath"/> (ADR 0017). The desktop has no sign-in of its own and no address of its own: it is the loopback of the namespace that
/// the two containers share, and the only way to it is this relay, behind the sign-in of the operator (the fallback policy closes every endpoint, this one included). The person opens it to sign in to the account by hand;
/// Geminarr neither types nor clicks in it. What the relay sends on is the request of the person, without the cookie of Geminarr's session, and what comes back has no cookie either.
/// </summary>
public static class BrowserRelay
{
    private const string PathPattern = BrowserOptions.RelayPath + "{**path}";
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(30);

    /// <summary>How often an open WebSocket looks at whether the session that opened it is still there.</summary>
    private static readonly TimeSpan SessionCheck = TimeSpan.FromSeconds(5);

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

            if (FromAnotherOrigin(context))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }

            // A WebSocket outlives the check that let it in: it ends with the session that opened it (signing out, the end of its lifetime), and does not go on for as long as it is used.
            using var stop = new CancellationTokenSource();
            var watching = context.WebSockets.IsWebSocketRequest
                ? EndWithTheSessionAsync(context, context.RequestServices.GetRequiredService<SessionRegistry>(), context.User.FindFirst(AccessConstants.SessionClaim)?.Value, stop.Token)
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
    /// Whether a page of another origin sends this request: the handshake of a WebSocket is not bound by the same-origin policy, and a write to the desktop (a POST, a PUT) is a write whoever sends it.
    /// The cookie of the session is <c>SameSite=Strict</c>; the origin is checked on top, where the browser says it (a request that names none is not from a page), against the names the page may be reached by.
    /// </summary>
    private static bool FromAnotherOrigin(HttpContext context)
    {
        var safe = HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method) || HttpMethods.IsOptions(context.Request.Method);
        if (safe && !context.WebSockets.IsWebSocketRequest)
        {
            return false;
        }

        if (context.Request.Headers.Origin.ToString() is not { Length: > 0 } origin)
        {
            return false;
        }

        // The names the page may be reached by (the same list that admits the Host, so a proxy that rewrites the Host header works): the origin of a page of the product is one of them, with any port.
        return !Uri.TryCreate(origin, UriKind.Absolute, out var uri) || !context.RequestServices.GetRequiredService<AllowedHostList>().IsAllowed(uri.Authority);
    }

    private static async Task EndWithTheSessionAsync(HttpContext context, SessionRegistry sessions, string? sessionId, CancellationToken stop)
    {
        using var timer = new PeriodicTimer(SessionCheck);
        try
        {
            while (await timer.WaitForNextTickAsync(stop))
            {
                if (!sessions.IsActive(sessionId))
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
