using Geminarr.Core.Connection;
using Geminarr.Core.Pipeline;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Geminarr.Api.V1.Connection;

/// <summary>The connection to the assistant: its state (SPEC §7.2), since when, the last pass that worked, why it is not simply connected, and which reader reads.</summary>
public sealed record ConnectionResource(string State, DateTimeOffset Since, DateTimeOffset? LastOkAt, string? Reason, string Reader);

/// <summary>The state of the connection, read only: the live view, pausing and disconnecting come with the vertical that signs in.</summary>
[ApiController]
[Route("api/v1/connection")]
public sealed class ConnectionController : ControllerBase
{
    private readonly ConnectionTracker _connection;
    private readonly IOptions<ReaderOptions> _reader;

    public ConnectionController(ConnectionTracker connection, IOptions<ReaderOptions> reader)
    {
        _connection = connection;
        _reader = reader;
    }

    [HttpGet]
    [ProducesResponseType<ConnectionResource>(StatusCodes.Status200OK)]
    public ConnectionResource Get()
    {
        var record = _connection.Current();
        return new ConnectionResource(record.State.ToString(), record.Since, record.LastOkAt, record.LastReason, _reader.Value.Kind.ToString());
    }
}
