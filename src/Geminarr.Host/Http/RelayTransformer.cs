using Geminarr.Core.Browser;
using Yarp.ReverseProxy.Forwarder;

namespace Geminarr.Host.Http;

/// <summary>What the relay sends on to the desktop, and what comes back (ADR 0017, 0019): the headers that its client needs and nothing else, its own origin, no cookie in either direction.</summary>
internal sealed class RelayTransformer : HttpTransformer
{
    private static readonly HashSet<string> ForwardedHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Accept", "Accept-Encoding", "Accept-Language", "Cache-Control", "Connection", "Pragma", "Range", "If-Match", "If-None-Match", "If-Modified-Since", "If-Unmodified-Since", "If-Range", "User-Agent", "Upgrade", "Referer", "Origin",
    };

    /// <summary>Whether a header of the person's request goes on to the desktop: the ones above, and the <c>Sec-</c> ones (the WebSocket handshake, what a browser says of how a request was made).</summary>
    private static bool IsForwardedHeader(string name) => ForwardedHeaders.Contains(name) || name.StartsWith("Sec-", StringComparison.OrdinalIgnoreCase);

    public static readonly RelayTransformer Instance = new();

    public override async ValueTask TransformRequestAsync(HttpContext httpContext, HttpRequestMessage proxyRequest, string destinationPrefix, CancellationToken cancellationToken)
    {
        await base.TransformRequestAsync(httpContext, proxyRequest, destinationPrefix, cancellationToken);
        // Geminarr's session is Geminarr's: the desktop never sees it. Nor does it see what a proxy in front of Geminarr adds about the operator (an identity, a token): the desktop is a third party's program, and
        // gets the headers that its client needs (what a browser sends for a page, a file and a WebSocket) and nothing else.
        foreach (var name in proxyRequest.Headers.Select(header => header.Key).Where(name => !IsForwardedHeader(name)).ToList())
        {
            proxyRequest.Headers.Remove(name);
        }
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
