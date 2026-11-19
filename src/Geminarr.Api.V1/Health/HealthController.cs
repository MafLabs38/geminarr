using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Geminarr.Api.V1.Health;

/// <summary>One check: its name, its state, the stable reason code that the interface maps to a sentence (it does not parse English), and the words.</summary>
public sealed record HealthCheckResource(string Name, string Status, string? Reason, string? Description);

/// <summary>The state of the container as its checks say it, worst first.</summary>
public sealed record HealthResource(string Status, IReadOnlyList<HealthCheckResource> Checks);

[ApiController]
[Route("api/v1/health")]
public sealed class HealthController : ControllerBase
{
    private readonly HealthCheckService _health;

    public HealthController(HealthCheckService health)
    {
        _health = health;
    }

    /// <summary>
    /// Every check, with its reason code. The probes of the platform (<c>/health/live</c>, <c>/health/ready</c>) answer the process and the readiness without a sign-in
    /// and say nothing more; this resource is for the operator. A description never holds a path, and a failure's exception is not shown.
    /// </summary>
    [HttpGet]
    [ProducesResponseType<HealthResource>(StatusCodes.Status200OK)]
    public async Task<HealthResource> Get(CancellationToken cancellationToken)
    {
        var report = await _health.CheckHealthAsync(cancellationToken);
        var checks = report.Entries
            .Select(entry => new HealthCheckResource(
                entry.Key,
                entry.Value.Status.ToString(),
                entry.Value.Data.TryGetValue("reason", out var reason) ? reason as string : null,
                entry.Value.Description))
            .OrderByDescending(check => Severity(check.Status)).ThenBy(check => check.Name, StringComparer.Ordinal)
            .ToList();
        return new HealthResource(report.Status.ToString(), checks);
    }

    private static int Severity(string status) => status switch
    {
        nameof(HealthStatus.Unhealthy) => 2,
        nameof(HealthStatus.Degraded) => 1,
        _ => 0,
    };
}
