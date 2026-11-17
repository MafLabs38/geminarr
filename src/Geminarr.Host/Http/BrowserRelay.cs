using System.Net;
using System.Text.RegularExpressions;
using Geminarr.Core.Access;
using Geminarr.Core.Browser;
using Geminarr.Core.Configuration;
using Geminarr.Host.Security;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;
using Yarp.ReverseProxy.Forwarder;

namespace Geminarr.Host.Http;

/// <summary>
/// Relays the web desktop of the browser container under <see cref="BrowserOptions.RelayPath"/>, on an origin of its own (ADR 0017, 0019). The desktop has no sign-in of its own and no address of its own: it is the loopback of the namespace that
/// the two containers share, and the only way to it is this relay, behind a cookie that the operator's session gave this origin by a ticket. The person opens it to sign in to the account by hand;
/// Geminarr neither types nor clicks in it. What the relay sends on is the request of the person, without any cookie of Geminarr's, and what comes back has no cookie either.
/// </summary>
public static class BrowserRelay
{
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(30);

    /// <summary>How often an open WebSocket looks at whether the session that opened it is still there.</summary>
    private static readonly TimeSpan SessionWatchInterval = TimeSpan.FromSeconds(5);

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

    /// <summary>The key, in <see cref="HttpContext.Items"/>, of a request that arrived on the desktop's origin.</summary>
    public const string OriginItemKey = "geminarr.desktop-origin";

    /// <summary>The path where a person arrives with a ticket: it sets the cookie of the desktop's origin and takes them to the desktop.</summary>
    public const string EnterPath = "/enter";

    /// <summary>
    /// The desktop is served on an origin of its own (ADR 0019): a second port of Geminarr, that answers for the desktop and for nothing else. The desktop is another program's, so its scripts never run where the operator's
    /// session cookie is sent. Nothing is served without the address of the desktop: the port is then not even open, and the path <see cref="BrowserOptions.RelayPath"/> is the interface's own on the operator's origin.
    /// </summary>
    public static WebApplication UseGeminarrDesktopOrigin(this WebApplication app)
    {
        var browser = app.Services.GetRequiredService<IOptions<BrowserOptions>>().Value;
        if (string.IsNullOrWhiteSpace(browser.DesktopUrl))
        {
            return app;
        }

        var destination = browser.DesktopUrl.TrimEnd('/') + "/";
        var forwarder = app.Services.GetRequiredService<IHttpForwarder>();
        var client = app.Services.GetRequiredService<RelayClient>().Invoker;
        var request = new ForwarderRequestConfig { ActivityTimeout = IdleTimeout };
        var port = browser.DesktopPort;
        app.MapWhen(context => context.Connection.LocalPort == port, branch =>
        {
            branch.Use((context, next) =>
            {
                context.Items[OriginItemKey] = true;
                return next(context);
            });
            branch.UseGeminarrSecurityHeaders();
            branch.UseMiddleware<AllowedHostsMiddleware>(true);
            branch.UseWebSockets();
            branch.Run(context => ServeAsync(context, forwarder, destination, client, request));
        });
        return app;
    }

    private static async Task ServeAsync(HttpContext context, IHttpForwarder forwarder, string destination, HttpMessageInvoker client, ForwarderRequestConfig request)
    {
        var services = context.RequestServices;
        var auth = services.GetRequiredService<IOptions<AuthOptions>>().Value;
        var access = services.GetRequiredService<DesktopAccess>();
        var cookieName = CookieName(auth);

        // A ticket is exchanged for the cookie of this origin, and the person goes on to the desktop without it in their address bar.
        if (context.Request.Path.Equals(EnterPath, StringComparison.OrdinalIgnoreCase) && HttpMethods.IsGet(context.Request.Method))
        {
            await EnterAsync(context, access, auth, cookieName);
            return;
        }

        if (!RelayRules.IsRelayPath(context.Request.Path))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        var desktopToken = context.Request.Cookies[cookieName];
        if (access.SessionOf(desktopToken) is null)
        {
            await WriteProblemAsync(context, StatusCodes.Status401Unauthorized, "Open the desktop from the Connection page of Geminarr.");
            return;
        }

        // The desktop is served under the path with its slash: without it, the person is sent there (routing matches both, so the endpoint tells them apart).
        if (string.Equals(context.Request.Path.Value, BrowserOptions.RelayPath.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
        {
            context.Response.Redirect(BrowserOptions.RelayPath);
            return;
        }

        // A desktop that could be reached by something else than Geminarr is not relayed: the person is told why, and the health says it (browser-exposed).
        if (services.GetRequiredService<IBrowserExposure>().Check() is { IsExposed: true } exposure)
        {
            await WriteProblemAsync(context, StatusCodes.Status503ServiceUnavailable, exposure.Describe());
            return;
        }

        // The desktop's client reads its pages and opens one WebSocket: no write goes over HTTP, and what else the image's web server offers (files, other services) is not relayed.
        if (!RelayRules.IsRelayed(context.Request))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        if (context.WebSockets.IsWebSocketRequest && RelayRules.IsFromAnotherOrigin(context.Request.Headers.Origin.ToString(), context.Request.Host, services.GetRequiredService<IOptions<BrowserOptions>>().Value.DesktopPublicUrl))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        // A WebSocket outlives the check that let it in: it ends with the session that opened it (signing out, the end of its lifetime), and does not go on for as long as it is used.
        using var stop = new CancellationTokenSource();
        var watching = context.WebSockets.IsWebSocketRequest
            ? EndWithTheSessionAsync(context, access, services.GetRequiredService<IBrowserExposure>(), services.GetRequiredService<TimeProvider>(), desktopToken, stop.Token)
            : Task.CompletedTask;
        try
        {
            var error = await forwarder.SendAsync(context, destination, client, request, RelayTransformer.Instance, context.RequestAborted);
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
    }

    private static async Task EnterAsync(HttpContext context, DesktopAccess access, AuthOptions auth, string cookieName)
    {
        if (access.Redeem(context.Request.Query["ticket"].ToString()) is not { } token)
        {
            await WriteProblemAsync(context, StatusCodes.Status401Unauthorized, "This link to the desktop is not valid any more. Open it again from the Connection page of Geminarr.");
            return;
        }

#pragma warning disable S2092 // Secure is the operator's own rule (CookieSecurity): always, or when the request was; a plain-HTTP sandbox cannot set a Secure cookie.
        context.Response.Cookies.Append(cookieName, token, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Strict,
            Secure = auth.CookieSecurity == CookieSecurityMode.Always || context.Request.IsHttps, // NOSONAR S2092: the operator's own rule (always, or when the request was)
            Path = "/",
            IsEssential = true,
        });
#pragma warning restore S2092
        context.Response.Redirect(BrowserOptions.RelayPath);
    }

    /// <summary>The name of the cookie of the desktop's origin: its own, so that the operator's cookie (which a browser also sends to another port of the same host) is never mistaken for it.</summary>
    public static string CookieName(AuthOptions auth) => auth.CookieSecurity == CookieSecurityMode.Always ? "__Host-geminarr.desktop" : "geminarr.desktop";

    private static Task WriteProblemAsync(HttpContext context, int status, string title)
    {
        context.Response.StatusCode = status;
        return context.Response.WriteAsJsonAsync(
            new Microsoft.AspNetCore.Mvc.ProblemDetails { Status = status, Title = title },
            options: null,
            contentType: "application/problem+json",
            cancellationToken: context.RequestAborted);
    }

    private static async Task EndWithTheSessionAsync(HttpContext context, DesktopAccess access, IBrowserExposure exposure, TimeProvider clock, string? desktopToken, CancellationToken stop)
    {
        using var timer = new PeriodicTimer(SessionWatchInterval, clock);
        try
        {
            while (await timer.WaitForNextTickAsync(stop))
            {
                // An open socket ends with the desktop's cookie that opened it (which ends with the operator's session), and with the desktop being closed: a namespace that became exposed after the socket was opened.
                if (access.SessionOf(desktopToken) is null || exposure.Check().IsExposed)
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
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // A watch that cannot do its job does not leave the socket open, and does not replace the relay's own result with its failure: the socket ends.
            context.Abort();
        }
    }

    /// <summary>The one client of the relay, in a type of its own so that no other service takes it for theirs.</summary>
    public sealed record RelayClient(HttpMessageInvoker Invoker);
}
