using Geminarr.Api.V1.Security;

namespace Geminarr.Host.Http;

public static class SecurityHeaders
{
    // Scripts, fonts, connections, frames and objects come from the application only; nothing is inline. The live view of the browser (a later
    // vertical) draws on a canvas and talks over a WebSocket to this very origin, which 'self' already covers.
    private const string ContentSecurityPolicy =
        "default-src 'self'; img-src 'self' data:; frame-ancestors 'none'; base-uri 'none'; form-action 'self'";

    // The pages of the browser's web desktop (ADR 0017) are another program's: they draw on a canvas, decode audio and video, start workers from blobs and talk over a WebSocket to this origin, and their own
    // scripts are inline. It is the only path that is given this much, on the origin that the desktop has to itself (ADR 0019), behind the cookie that a ticket gave it, and it is never framed.
    private const string RelayContentSecurityPolicy =
        "default-src 'self'; script-src 'self' 'unsafe-inline' 'unsafe-eval' blob:; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob:; media-src 'self' blob:; "
        + "connect-src 'self' blob: data:; worker-src 'self' blob:; font-src 'self' data:; frame-ancestors 'none'; base-uri 'self'; form-action 'self'; object-src 'none'";

    /// <summary>
    /// Conservative headers on every response, errors included: they are set when the response starts, because the
    /// exception handler clears the response, headers and all, before it writes its own.
    /// HSTS belongs to the reverse proxy that terminates TLS.
    /// </summary>
    public static IApplicationBuilder UseGeminarrSecurityHeaders(this IApplicationBuilder app) =>
        app.Use((context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                var headers = context.Response.Headers;
                headers["X-Content-Type-Options"] = "nosniff";
                headers["X-Frame-Options"] = "DENY";
                headers["Referrer-Policy"] = "no-referrer";
                // Some endpoints keep their own headers when they are served: the logo of the identity is an image that is the same until its hash changes, with the strictest policy,
                // and the browser of the person keeps it (ADR 0013). An error of the same endpoint has the application's policy and is not cached, like every other answer.
                // The endpoint must also have said a policy: the marker alone does not leave an answer without one.
                var keepsOwnHeaders = context.Response.StatusCode is StatusCodes.Status200OK or StatusCodes.Status304NotModified
                    && context.GetEndpoint()?.Metadata.GetMetadata<KeepsItsOwnHeadersAttribute>() is not null
                    && headers.ContainsKey("Content-Security-Policy");
                if (!keepsOwnHeaders)
                {
                    headers["Content-Security-Policy"] = context.Items.ContainsKey(BrowserRelay.OriginItemKey) && BrowserRelay.IsRelayPath(context.Request.Path) ? RelayContentSecurityPolicy : ContentSecurityPolicy;
                }

                if (ApiPaths.IsApi(context.Request.Path) && !keepsOwnHeaders)
                {
                    // What the API says is for the person who asked: no cache keeps it.
                    headers.CacheControl = "no-store";
                    headers.Pragma = "no-cache";
                }

                return Task.CompletedTask;
            });
            return next();
        });
}
