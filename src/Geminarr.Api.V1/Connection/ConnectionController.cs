using Geminarr.Core.Browser;
using Geminarr.Core.Connection;
using Geminarr.Core.Pipeline;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Geminarr.Api.V1.Connection;

/// <summary>The connection to the assistant: its state (SPEC §7.2), since when, the last pass that worked, why it is not simply connected, which reader reads, whether a browser reads, and until when the passes are paused.</summary>
public sealed record ConnectionResource(string State, DateTimeOffset Since, DateTimeOffset? LastOkAt, string? Reason, string Reader, bool HasBrowser, DateTimeOffset? PausedUntil, int LiveViewMinutes);

/// <summary>A live view that was asked for: where to connect (a WebSocket on this origin), the single-use token to present, the size of the screen and until when it lasts.</summary>
public sealed record LiveViewResource(string Id, string Token, string Path, DateTimeOffset ExpiresAt, int Width, int Height);

/// <summary>How long to pause: hours, or until the operator resumes when it is missing.</summary>
public sealed record PauseRequest(double? Hours);

/// <summary>The connection to the assistant: its state, the live view where the operator signs in, pausing, and disconnecting (F-CON-01 to F-CON-04).</summary>
[ApiController]
[Route("api/v1/connection")]
public sealed class ConnectionController : ControllerBase
{
    /// <summary>The longest pause: a quiet that is meant to be longer is a disconnect.</summary>
    private const double MaxPauseHours = 24 * 30;

    private readonly ConnectionService _connection;
    private readonly IOptions<ReaderOptions> _reader;
    private readonly IOptions<BrowserOptions> _browser;

    public ConnectionController(ConnectionService connection, IOptions<ReaderOptions> reader, IOptions<BrowserOptions> browser)
    {
        _connection = connection;
        _reader = reader;
        _browser = browser;
    }

    [HttpGet]
    [ProducesResponseType<ConnectionResource>(StatusCodes.Status200OK)]
    public ConnectionResource Get()
    {
        var record = _connection.Current();
        return new ConnectionResource(record.State.ToString(), record.Since, record.LastOkAt, record.LastReason, _reader.Value.Kind.ToString(), _connection.HasBrowser, record.PausedUntil, _browser.Value.LiveViewMinutes);
    }

    /// <summary>Asks for a live view: the browser's screen on the page, where the operator signs in to the dedicated account. One at a time, ten minutes at most.</summary>
    [HttpPost("live-views")]
    [ProducesResponseType<LiveViewResource>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public ActionResult<LiveViewResource> StartLiveView()
    {
        try
        {
            var ticket = _connection.StartSignIn();
            return new LiveViewResource(ticket.Id, ticket.Token, $"/api/v1/connection/live-views/{ticket.Id}", ticket.ExpiresAt, _browser.Value.ViewportWidth, _browser.Value.ViewportHeight);
        }
        catch (LiveViewBusyException ex)
        {
            return Problem(statusCode: StatusCodes.Status409Conflict, title: ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: ex.Message);
        }
    }

    /// <summary>Closes the live view.</summary>
    [HttpDelete("live-views")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public IActionResult CloseLiveView()
    {
        _connection.CloseLiveView();
        return NoContent();
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
        catch (InvalidOperationException ex)
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

    /// <summary>Closes the browser and deletes the profile: the session is gone, and the state is not connected. What waits for Mnemarr stays.</summary>
    [HttpPost("disconnect")]
    [ProducesResponseType<ConnectionResource>(StatusCodes.Status200OK)]
    public async Task<ConnectionResource> Disconnect(CancellationToken cancellationToken)
    {
        await _connection.DisconnectAsync(cancellationToken);
        return Get();
    }
}
