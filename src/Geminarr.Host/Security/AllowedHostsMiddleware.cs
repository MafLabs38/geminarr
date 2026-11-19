using Geminarr.Core.Access;
using Geminarr.Host.Health;
using Microsoft.AspNetCore.Mvc;

namespace Geminarr.Host.Security;

/// <summary>
/// Refuses a request whose <c>Host</c> is not one the page may be reached by, so that a web page that points its own name at this address (DNS rebinding) cannot
/// make the operator's browser read what this container says as if it were its own origin. The probes are exempt: a platform calls them by the address of the pod.
/// </summary>
public sealed class AllowedHostsMiddleware
{
    private readonly RequestDelegate _next;
    private readonly AllowedHostList _hosts;
    private readonly bool _desktopOrigin;

    /// <param name="next">The next middleware.</param>
    /// <param name="hosts">The names the page may be reached by.</param>
    /// <param name="desktopOrigin">True on the desktop's own origin, which also answers to the host that a proxy gives it.</param>
    public AllowedHostsMiddleware(RequestDelegate next, AllowedHostList hosts, bool desktopOrigin = false)
    {
        _next = next;
        _hosts = hosts;
        _desktopOrigin = desktopOrigin;
    }

    public Task InvokeAsync(HttpContext context)
    {
        if (HealthExtensions.IsProbe(context.Request.Path) || (_desktopOrigin ? _hosts.IsAllowedOnDesktopOrigin(context.Request.Host.Value) : _hosts.IsAllowed(context.Request.Host.Value)))
        {
            return _next(context);
        }

        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        return context.Response.WriteAsJsonAsync(
            new ProblemDetails { Status = StatusCodes.Status400BadRequest, Title = "The host name is not allowed." },
            options: null,
            contentType: "application/problem+json",
            cancellationToken: context.RequestAborted);
    }
}
