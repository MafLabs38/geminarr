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
        var origin = OriginOfTheExtension();
        if (origin is null || !context.Request.Path.Equals(ExtensionRoutes.PushPath, StringComparison.OrdinalIgnoreCase) || !string.Equals(context.Request.Headers.Origin.ToString(), origin, StringComparison.Ordinal))
        {
            return _next(context);
        }

        // The header says the origin that the package gives, never what the request said it was.
        var headers = context.Response.Headers;
        headers.AccessControlAllowOrigin = origin;
        headers.Append(HeaderNames.Vary, "Origin");
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
