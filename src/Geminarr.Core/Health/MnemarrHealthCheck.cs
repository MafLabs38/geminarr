using Geminarr.Core.Link;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Geminarr.Core.Health;

/// <summary>The destination: linked, or why not. Degraded, never unhealthy: Geminarr is useful to look at, and what waits is kept.</summary>
public sealed class MnemarrHealthCheck : IHealthCheck
{
    private readonly LinkService _link;

    public MnemarrHealthCheck(LinkService link)
    {
        _link = link;
    }

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            return Task.FromResult(_link.Status().Problem switch
            {
                null => HealthCheckResult.Healthy(),
                DestinationProblems.NotLinked => HealthReasons.Degraded(DestinationProblems.NotLinked, "No Mnemarr is linked: nothing is sent."),
                DestinationProblems.InsecureAddress => HealthReasons.Degraded(DestinationProblems.InsecureAddress, "The address of Mnemarr is not https: nothing is sent."),
                DestinationProblems.Refused => HealthReasons.Degraded(DestinationProblems.Refused, "Mnemarr refused the key of this machine: pair again."),
                DestinationProblems.Rejected => HealthReasons.Degraded(DestinationProblems.Rejected, "Mnemarr said no to what was sent."),
                _ => HealthReasons.Degraded(DestinationProblems.Unreachable, "Mnemarr does not answer: what waits is kept."),
            });
        }
        catch (Exception ex) when (Storage.StorageErrors.IsUnavailable(ex))
        {
            return Task.FromResult(HealthReasons.Degraded(HealthReasons.DatabaseUnreadable, "The destination cannot be read: the database cannot be reached."));
        }
    }
}
