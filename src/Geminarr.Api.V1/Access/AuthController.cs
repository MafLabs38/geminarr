using System.Security.Claims;
using Geminarr.Core.Access;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Geminarr.Api.V1.Access;

/// <summary>A name and a password: what the interface sends to sign in. A missing one is the same as a wrong one.</summary>
public sealed record LoginRequest(string? Name, string? Password);

/// <summary>Who is signed in. The interface shows the name; the server decides what is allowed on its own.</summary>
public sealed record SessionResource(string Name);

/// <summary>Signing in and out of the interface, and who is signed in.</summary>
[ApiController]
[Route("api/v1/auth")]
public sealed partial class AuthController : ControllerBase
{
    /// <summary>A name and a password are short: a body larger than this is not a sign-in.</summary>
    private const int MaxBodyBytes = 8 * 1024;

    private readonly SignInService _signIn;
    private readonly SessionRegistry _sessions;
    private readonly TimeProvider _clock;
    private readonly ILogger<AuthController> _logger;

    public AuthController(SignInService signIn, SessionRegistry sessions, TimeProvider clock, ILogger<AuthController> logger)
    {
        _signIn = signIn;
        _sessions = sessions;
        _clock = clock;
        _logger = logger;
    }

    // What is said about a sign-in is the outcome and nothing of what was typed: a name or a password is not something to keep in a log.
    [LoggerMessage(Level = LogLevel.Warning, Message = "A sign-in was refused: the name or the password is wrong.")]
    private partial void LogRefused();

    [LoggerMessage(Level = LogLevel.Warning, Message = "A sign-in was refused: the name is locked for {Seconds} seconds after too many failures.")]
    private partial void LogLocked(int seconds);

    [LoggerMessage(Level = LogLevel.Information, Message = "The operator signed in.")]
    private partial void LogSignedIn();

    /// <summary>Signs the operator in and starts a session. The answer is the same for a wrong name and a wrong password.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [RequestSizeLimit(MaxBodyBytes)]
    [ProducesResponseType<SessionResource>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<SessionResource>> Login(LoginRequest request)
    {
        var outcome = _signIn.SignIn(request.Name, request.Password);
        if (outcome.Status == SignInStatus.Locked)
        {
            var seconds = (int)Math.Ceiling((outcome.RetryAfter ?? LoginThrottle.Window).TotalSeconds);
            LogLocked(seconds);
            Response.Headers.RetryAfter = seconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return Problem(statusCode: StatusCodes.Status429TooManyRequests, title: "Too many failed attempts. Try again later.");
        }

        if (outcome.Status != SignInStatus.Signed || outcome.SessionId is null)
        {
            LogRefused();
            return Problem(statusCode: StatusCodes.Status401Unauthorized, title: "The name or the password is wrong.");
        }

        // A caller who signs in again does not keep the session of the cookie they had: a copy of that cookie is dead from now on.
        _signIn.SignOut(User.FindFirstValue(AccessConstants.SessionClaim));
        var name = request.Name!.Trim();
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, name), new Claim(AccessConstants.SessionClaim, outcome.SessionId)], AccessConstants.Scheme);
        var properties = new AuthenticationProperties { IsPersistent = false, ExpiresUtc = _clock.GetUtcNow() + _sessions.Lifetime };
        await HttpContext.SignInAsync(AccessConstants.Scheme, new ClaimsPrincipal(identity), properties);
        LogSignedIn();
        return new SessionResource(name);
    }

    /// <summary>Ends the session: the cookie is dead on the server, not only dropped by the browser.</summary>
    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout()
    {
        _signIn.SignOut(User.FindFirstValue(AccessConstants.SessionClaim));
        await HttpContext.SignOutAsync(AccessConstants.Scheme);
        return NoContent();
    }

    /// <summary>Who is signed in. Answered 401 when nobody is.</summary>
    [HttpGet("session")]
    [ProducesResponseType<SessionResource>(StatusCodes.Status200OK)]
    public SessionResource GetSession() => new(User.Identity?.Name ?? string.Empty);
}
