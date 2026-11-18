using Geminarr.Core.Pipeline;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Geminarr.Api.V1.Runs;

/// <summary>One pass, as the page shows it: when, how it ended and why, and the counts. Nothing a conversation said.</summary>
public sealed record RunResource(long Id, string Kind, DateTimeOffset StartedAt, DateTimeOffset? EndedAt, string? Outcome, string? Reason, int Listed, int Opened, int TurnsSent, long DurationMs)
{
    public static RunResource From(RunRecord run) => new(run.Id, run.Kind, run.StartedAt, run.EndedAt, run.Outcome, run.Reason, run.Listed, run.Opened, run.TurnsSent, run.DurationMs);
}

public sealed record RunsResource(IReadOnlyList<RunResource> Runs, bool Running);

/// <summary>The passes (F-RDR-07): the latest ones, and "run now".</summary>
[ApiController]
[Route("api/v1/runs")]
public sealed class RunsController : ControllerBase
{
    /// <summary>The most runs one call returns.</summary>
    private const int MaxLimit = 100;

    private readonly IRunStore _runs;
    private readonly PassRunner _runner;
    private readonly PassTrigger _trigger;

    public RunsController(IRunStore runs, PassRunner runner, PassTrigger trigger)
    {
        _runs = runs;
        _runner = runner;
        _trigger = trigger;
    }

    /// <summary>The latest runs, newest first, and whether one is running now.</summary>
    [HttpGet]
    [ProducesResponseType<RunsResource>(StatusCodes.Status200OK)]
    public RunsResource Get([FromQuery] int limit = 20) =>
        new([.. _runs.Recent(Math.Clamp(limit, 1, MaxLimit)).Select(RunResource.From)], _runner.IsRunning);

    /// <summary>Asks for a pass now instead of waiting for the next one. Answered at once: the pass runs in the background.</summary>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public IActionResult RunNow()
    {
        if (_runner.IsRunning)
        {
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "A pass is running.");
        }

        _trigger.Request();
        return Accepted();
    }
}
