using Geminarr.Core.Browser;
using Geminarr.Core.Connection;
using Geminarr.Core.Pipeline;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Geminarr.Api.V1.Connection;

/// <summary>The connection to the assistant: its state (SPEC §7.2), since when, the last pass that worked, why it is not simply connected, which reader reads, whether a browser reads, and until when the passes are paused.</summary>
public sealed record ConnectionResource(string State, DateTimeOffset Since, DateTimeOffset? LastOkAt, string? Reason, string Reader, bool HasBrowser, DateTimeOffset? PausedUntil, string? BrowserPagePath, AccountResource? Account);

/// <summary>The account that the browser is signed in as: an address, and a name when the page says one. For the operator's page only.</summary>
public sealed record AccountResource(string? Name, string Email);

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
        return new ConnectionResource(record.State.ToString(), record.Since, record.LastOkAt, record.LastReason, _reader.Value.Kind.ToString(), _connection.HasBrowser, record.PausedUntil, string.IsNullOrWhiteSpace(_browser.Value.DesktopUrl) ? null : BrowserOptions.RelayPath, _connection.Account is { } account ? new AccountResource(account.Name, account.Email) : null);
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

    /// <summary>Clears the session that the browser holds: the state is not connected, and a person signs in again by hand. What waits for Mnemarr stays.</summary>
    [HttpPost("disconnect")]
    [ProducesResponseType<ConnectionResource>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<ConnectionResource>> Disconnect(CancellationToken cancellationToken)
    {
        try
        {
            await _connection.DisconnectAsync(cancellationToken);
        }
        catch (ReaderException)
        {
            // The connection is forgotten, but the browser could not be reached: the session stays in it until it is cleared (the sentence is ours, never an address).
            return Problem(statusCode: StatusCodes.Status502BadGateway, title: "The browser container could not be reached: the connection is forgotten here, but the session stays in the browser until it is signed out there.");
        }

        return Get();
    }
}
