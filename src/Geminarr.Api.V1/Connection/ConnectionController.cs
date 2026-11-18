using Geminarr.Api.V1.Access;
using Geminarr.Core.Access;
using Geminarr.Core.Browser;
using Geminarr.Core.Configuration;
using Geminarr.Core.Connection;
using Geminarr.Core.Pipeline;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Geminarr.Api.V1.Connection;

/// <summary>The connection to the assistant: its state (SPEC §7.2), since when, the last pass that worked, why it is not simply connected, which reader reads, whether a browser reads, and until when the passes are paused.</summary>
public sealed record ConnectionResource(string State, DateTimeOffset Since, DateTimeOffset? LastOkAt, string? Reason, string Reader, bool HasBrowser, DateTimeOffset? PausedUntil, bool HasDesktop, AccountResource? Account, bool SigningIn, bool SignOutPending);

/// <summary>The account that the browser is signed in as: an address, and a name when the page says one. For the operator's page only.</summary>
public sealed record AccountResource(string? Name, string Email);

/// <summary>Where to open the desktop of the browser: its own origin, with a ticket that is worth one opening (ADR 0019).</summary>
public sealed record DesktopResource(string Url);

/// <summary>How long to pause: hours, or until the operator resumes when it is missing.</summary>
public sealed record PauseRequest(double? Hours);

/// <summary>The connection to the assistant: its state, pausing and disconnecting; a person signs in to the browser by hand in its own page (ADR 0016) (F-CON-01 to F-CON-04).</summary>
[ApiController]
[Route("api/v1/connection")]
public sealed class ConnectionController : ControllerBase
{
    /// <summary>The longest pause: a quiet that is meant to be longer is a disconnect.</summary>
    private const double MaxPauseHours = 24 * 30;

    private readonly ConnectionService _connection;
    private readonly IOptions<ReaderOptions> _reader;
    private readonly IOptions<BrowserOptions> _browser;
    private readonly IOptions<GeminarrOptions> _app;
    private readonly DesktopAccess _desktop;

    public ConnectionController(ConnectionService connection, IOptions<ReaderOptions> reader, IOptions<BrowserOptions> browser, IOptions<GeminarrOptions> app, DesktopAccess desktop)
    {
        _connection = connection;
        _reader = reader;
        _browser = browser;
        _app = app;
        _desktop = desktop;
    }

    [HttpGet]
    [ProducesResponseType<ConnectionResource>(StatusCodes.Status200OK)]
    public ConnectionResource Get()
    {
        var record = _connection.Current();
        return new ConnectionResource(record.State.ToString(), record.Since, record.LastOkAt, record.LastReason, _reader.Value.Kind.ToString(), _connection.HasBrowser, record.PausedUntil, !string.IsNullOrWhiteSpace(_browser.Value.DesktopUrl), _connection.AccountAt(record) is { } account ? new AccountResource(account.Name, account.Email) : null, _connection.SigningInAt(record), _connection.SignOutPending);
    }

    /// <summary>
    /// Gives the address to open the desktop of the browser by: the desktop is served on an origin of its own, and this address carries a ticket that is worth one opening and a minute (ADR 0019). The page opens it in a window.
    /// </summary>
    [HttpPost("desktop")]
    [ProducesResponseType<DesktopResource>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public ActionResult<DesktopResource> OpenDesktop()
    {
        if (string.IsNullOrWhiteSpace(_browser.Value.DesktopUrl))
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "No desktop of the browser is configured.");
        }

        var ticket = _desktop.IssueTicket(User.FindFirst(AccessConstants.SessionClaim)?.Value);
        if (ticket is null)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "The session is not one that is open any more: sign in again.");
        }

        // A person is about to sign in by hand in that desktop: the extension is asked to look often until it sees somebody signed in.
        _connection.BeginSigningIn();

        var origin = DesktopAddress.Origin(_browser.Value, _app.Value.PublicUrl, Request.Scheme, Request.Host.Host);
        return new DesktopResource($"{origin}/enter?ticket={Uri.EscapeDataString(ticket)}");
    }

    /// <summary>Quiet: no pass reads until the moment given, or until the operator resumes.</summary>
    [HttpPost("pause")]
    [ProducesResponseType<ConnectionResource>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public ActionResult<ConnectionResource> Pause(PauseRequest request)
    {
        if (request.Hours is { } hours && (hours <= 0 || hours > MaxPauseHours || double.IsNaN(hours)))
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "A pause lasts more than nothing and at most thirty days.");
        }

        try
        {
            _connection.Pause(request.Hours is { } span ? TimeSpan.FromHours(span) : null);
        }
        catch (ConnectionRefusedException ex)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: ex.Message);
        }

        return Get();
    }

    [HttpPost("resume")]
    [ProducesResponseType<ConnectionResource>(StatusCodes.Status200OK)]
    public ConnectionResource Resume()
    {
        _connection.Resume();
        return Get();
    }

    /// <summary>Gives up a sign-out that the extension has not confirmed (the browser may be off, or it refused): the account that is signed in stays. Nothing happens when no sign-out is pending.</summary>
    [HttpPost("disconnect/cancel")]
    [ProducesResponseType<ConnectionResource>(StatusCodes.Status200OK)]
    public ConnectionResource CancelDisconnect()
    {
        _connection.CancelDisconnect();
        return Get();
    }

    /// <summary>Signs the account out of the browser and forgets the connection: the state is not connected, and a person signs in again by hand. What waits for Mnemarr stays. The extension of the browser does it, the next time that it says that it is there (nothing else of the browser is touched).</summary>
    [HttpPost("disconnect")]
    [ProducesResponseType<ConnectionResource>(StatusCodes.Status200OK)]
    public ConnectionResource Disconnect()
    {
        _connection.Disconnect();
        return Get();
    }
}
