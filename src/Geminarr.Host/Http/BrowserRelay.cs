using System.Net;
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
    private const string PathPattern = "/browser/{**path}";
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(30);

    /// <summary>Whether the path is the relay's: its pages draw the screen of another program and need other headers than Geminarr's own (see <see cref="SecurityHeaders"/>).</summary>
    public static bool IsRelayPath(PathString path) => path.StartsWithSegments(BrowserOptions.RelayPath.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);

    public static IServiceCollection AddGeminarrBrowserRelay(this IServiceCollection services)
    {
        services.AddHttpForwarder();
        services.AddSingleton(new RelayClient(new HttpMessageInvoker(new SocketsHttpHandler
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

            if (IsForeignWebSocket(context))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }

            var error = await forwarder.SendAsync(context, destination, client, request, RelayTransformer.Instance);
            if (error != ForwarderError.None && !context.Response.HasStarted)
            {
                context.Response.StatusCode = StatusCodes.Status502BadGateway;
            }
        });
        return app;
    }

    /// <summary>A WebSocket that a page of another origin opens: the cookie of the session is <c>SameSite=Strict</c>, and the origin is checked all the same, as the handshake of a WebSocket is not bound by the same-origin policy.</summary>
    private static bool IsForeignWebSocket(HttpContext context) =>
        context.WebSockets.IsWebSocketRequest
        && context.Request.Headers.Origin.ToString() is { Length: > 0 } origin
        && (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) || !string.Equals(uri.Authority, context.Request.Host.Value, StringComparison.OrdinalIgnoreCase));

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
        }

        public override async ValueTask<bool> TransformResponseAsync(HttpContext httpContext, HttpResponseMessage? proxyResponse, CancellationToken cancellationToken)
        {
            var proceed = await base.TransformResponseAsync(httpContext, proxyResponse, cancellationToken);
            httpContext.Response.Headers.Remove("Set-Cookie");
            return proceed;
        }
    }
}
