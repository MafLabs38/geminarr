using Geminarr.Core.SystemInfo;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Geminarr.Api.V1.SystemInfo;

/// <summary>
/// Foundation vertical: this is all the API says, and it says nothing sensitive (version, runtime, uptime). Access control arrives with the
/// first vertical, before anything about a session or a conversation exists (docs/ROADMAP.md).
/// </summary>
[ApiController]
[Route("api/v1/system")]
public sealed class SystemController : ControllerBase
{
    private readonly ISystemStatusService _systemStatusService;

    public SystemController(ISystemStatusService systemStatusService)
    {
        _systemStatusService = systemStatusService;
    }

    /// <summary>Version, runtime and uptime of this Geminarr.</summary>
    [HttpGet("status")]
    [ProducesResponseType<SystemStatusResource>(StatusCodes.Status200OK)]
    public SystemStatusResource GetStatus() => _systemStatusService.GetStatus().ToResource();
}
