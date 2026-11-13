using Geminarr.Api.V1.Access;
using Geminarr.Core.Access;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace Geminarr.Host.Security;

public static class SecurityExtensions
{
    /// <summary>
    /// Sign-in by cookie for the operator, and a fallback policy that requires a sign-in everywhere else: an endpoint nobody thought about is closed. The keys that
    /// sign the cookies are in memory, with the sessions: a restart signs everybody out.
    /// </summary>
    public static IServiceCollection AddGeminarrSecurity(this IServiceCollection services)
    {
        services.AddDataProtection().UseEphemeralDataProtectionProvider();
        services.AddAuthentication(AccessConstants.Scheme).AddCookie(AccessConstants.Scheme);
        services.AddOptions<CookieAuthenticationOptions>(AccessConstants.Scheme).Configure<IOptions<AuthOptions>, TimeProvider>((cookie, auth, clock) =>
        {
            ConfigureCookie(cookie, auth.Value);
            cookie.TimeProvider = clock;
        });
        services.AddAuthorization(options => options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
        return services;
    }

    private static void ConfigureCookie(CookieAuthenticationOptions cookie, AuthOptions auth)
    {
        var always = auth.CookieSecurity == CookieSecurityMode.Always;
        // The __Host- prefix makes the browser refuse a cookie of that name set by a sibling host or with a Domain: it needs Secure.
        cookie.Cookie.Name = always ? "__Host-geminarr.session" : "geminarr.session";
        cookie.Cookie.HttpOnly = true;
        cookie.Cookie.SameSite = SameSiteMode.Strict;
        cookie.Cookie.SecurePolicy = always ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
        cookie.ExpireTimeSpan = TimeSpan.FromHours(auth.SessionHours);
        cookie.SlidingExpiration = false;
        cookie.Events.OnValidatePrincipal = ValidateSession;
        // An API answers with a status, never with a redirect to a login page.
        cookie.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        cookie.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    }

    /// <summary>A cookie is valid while its session is: signing out and the end of the lifetime end it on the server, and a cookie made from a copy of the keys has no session behind it.</summary>
    private static async Task ValidateSession(CookieValidatePrincipalContext context)
    {
        var sessions = context.HttpContext.RequestServices.GetRequiredService<SessionRegistry>();
        if (!sessions.IsActive(context.Principal?.FindFirst(AccessConstants.SessionClaim)?.Value))
        {
            context.RejectPrincipal();
            // The browser is told to drop the cookie, so that it does not send a dead one until it expires.
            await context.HttpContext.SignOutAsync(AccessConstants.Scheme);
        }
    }

    /// <summary>The hosts, then who is who, then the header of a write, then what each may do.</summary>
    public static WebApplication UseGeminarrSecurity(this WebApplication app)
    {
        app.UseAuthentication();
        app.UseMiddleware<RequestIntegrityMiddleware>();
        app.UseAuthorization();
        return app;
    }
}
