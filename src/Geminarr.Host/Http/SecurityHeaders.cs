namespace Geminarr.Host.Http;

public static class SecurityHeaders
{
    // Scripts, fonts, connections, frames and objects come from the application only; nothing is inline. The live view of the browser (a later
    // vertical) draws on a canvas and talks over a WebSocket to this very origin, which 'self' already covers.
    private const string ContentSecurityPolicy =
        "default-src 'self'; img-src 'self' data:; frame-ancestors 'none'; base-uri 'none'; form-action 'self'";

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
                headers["Content-Security-Policy"] = ContentSecurityPolicy;
                if (ApiPaths.IsApi(context.Request.Path))
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
