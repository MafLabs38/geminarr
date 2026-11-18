using Geminarr.Api.V1.Extension;
using Geminarr.Core.Extension;
using Microsoft.Net.Http.Headers;

namespace Geminarr.Host.Security;

/// <summary>
/// Lets the extension, and only it, read the answer of the push route from the page of its own origin (<c>chrome-extension://&lt;id&gt;</c>, the id being the one of the package that this image carries). Any other
/// origin gets no header, so a page of the web cannot read the route; the token is what guards it. A preflight is answered here, before the sign-in, for the route of the push and nothing else.
/// </summary>
public sealed class ExtensionCorsMiddleware
{
    private const string AllowedHeaders = "authorization, content-type, x-geminarr-request";
    private const string PreflightMaxAgeSeconds = "600";

    private readonly RequestDelegate _next;
    private readonly IExtensionPackage _package;

    public ExtensionCorsMiddleware(RequestDelegate next, IExtensionPackage package)
    {
        _next = next;
        _package = package;
    }

    public Task InvokeAsync(HttpContext context)
    {
        // Only a request for the push route that names an origin reads the package: every other request of the host goes through without a look at the disk.
        if (!context.Request.Path.Equals(ExtensionRoutes.PushPath, StringComparison.OrdinalIgnoreCase))
        {
            return _next(context);
        }

        var headers = context.Response.Headers;
        // The answer depends on the origin whatever it is: a cache between must not serve one origin's answer to another.
        headers.Append(HeaderNames.Vary, "Origin");
        // A request that names no origin is not the extension's: the package is not read for it.
        var origin = context.Request.Headers.Origin.Count == 0 ? null : OriginOfTheExtension();
        if (origin is null || !string.Equals(context.Request.Headers.Origin.ToString(), origin, StringComparison.Ordinal))
        {
            return _next(context);
        }

        // The header says the origin that the package gives, never what the request said it was.
        headers.AccessControlAllowOrigin = origin;
        if (!HttpMethods.IsOptions(context.Request.Method))
        {
            return _next(context);
        }

        headers.AccessControlAllowMethods = "POST";
        headers.AccessControlAllowHeaders = AllowedHeaders;
        headers.AccessControlMaxAge = PreflightMaxAgeSeconds;
        if (string.Equals(context.Request.Headers["Access-Control-Request-Private-Network"], "true", StringComparison.OrdinalIgnoreCase))
        {
            headers["Access-Control-Allow-Private-Network"] = "true";
        }

        context.Response.StatusCode = StatusCodes.Status204NoContent;
        return Task.CompletedTask;
    }

    private string? OriginOfTheExtension() => _package.Current() is { } package ? $"chrome-extension://{package.Id}" : null;
}
