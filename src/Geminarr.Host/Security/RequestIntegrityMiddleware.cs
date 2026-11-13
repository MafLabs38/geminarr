using Geminarr.Host.Http;
using Microsoft.AspNetCore.Mvc;

namespace Geminarr.Host.Security;

/// <summary>
/// The protection against cross-site requests, on top of <c>SameSite=Strict</c>: a write to the API must carry the custom header that only the interface, and a
/// tool that means to, sends. A browser cannot add it to a form or a link. It is checked before the sign-in itself (a write without it is refused whoever sends it).
/// </summary>
public sealed class RequestIntegrityMiddleware
{
    public const string HeaderName = "X-Geminarr-Request";

    private static readonly HashSet<string> SafeMethods = new(StringComparer.OrdinalIgnoreCase) { "GET", "HEAD", "OPTIONS" };
    private readonly RequestDelegate _next;

    public RequestIntegrityMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public Task InvokeAsync(HttpContext context)
    {
        if (!SafeMethods.Contains(context.Request.Method) && ApiPaths.IsApi(context.Request.Path) && !context.Request.Headers.ContainsKey(HeaderName))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return context.Response.WriteAsJsonAsync(
                new ProblemDetails { Status = StatusCodes.Status403Forbidden, Title = $"A write needs the {HeaderName} header." },
                options: null,
                contentType: "application/problem+json",
                cancellationToken: context.RequestAborted);
        }

        return _next(context);
    }
}
