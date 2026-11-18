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
public sealed record ConnectionResource(string State, DateTimeOffset Since, DateTimeOffset? LastOkAt, string? Reason, string Reader, bool HasBrowser, DateTimeOffset? PausedUntil, bool HasDesktop);

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
        return new ConnectionResource(record.State.ToString(), record.Since, record.LastOkAt, record.LastReason, _reader.Value.Kind.ToString(), _connection.HasBrowser, record.PausedUntil, !string.IsNullOrWhiteSpace(_browser.Value.DesktopUrl));
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

    /// <summary>Clears the session that the browser holds: the state is not connected, and a person signs in again by hand. What waits for Mnemarr stays. Refused (400) by a Geminarr that has no debugging port to the browser (ADR 0021).</summary>
    [HttpPost("disconnect")]
    [ProducesResponseType<ConnectionResource>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<ConnectionResource>> Disconnect(CancellationToken cancellationToken)
    {
        try
        {
            await _connection.DisconnectAsync(cancellationToken);
        }
        catch (ConnectionRefusedException ex)
        {
            // This version cannot clear the session (ADR 0021: that comes with the extension reader): the sentence is ours and nothing changed.
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: ex.Message);
        }
        catch (ReaderException)
        {
            // Nothing changed: the session is still in the browser (the sentence is ours, never an address).
            return Problem(statusCode: StatusCodes.Status502BadGateway, title: "The browser container could not be reached: nothing was disconnected. Try again when it is back.");
        }

        return Get();
    }
}
