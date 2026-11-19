using Geminarr.Core.Access;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Geminarr.Core.Health;

/// <summary>
/// Whether somebody can sign in. Degraded, never unhealthy: the container is alive and safe (everything but the probes is closed) and the probe of the
/// platform must not restart it for a missing secret. The descriptions say what is wrong without a path or a name.
/// </summary>
public sealed class OperatorHealthCheck : IHealthCheck
{
    private readonly IOperatorCredentials _credentials;

    public OperatorHealthCheck(IOperatorCredentials credentials)
    {
        _credentials = credentials;
    }

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(_credentials.Status switch
        {
            OperatorStatus.Ready => HealthCheckResult.Healthy(),
            OperatorStatus.NotConfigured => HealthReasons.Degraded(HealthReasons.OperatorNotConfigured, "No operator is configured: nobody can sign in."),
            OperatorStatus.PasswordUnreadable => HealthReasons.Degraded(HealthReasons.OperatorPasswordUnreadable, "The password file of the operator cannot be read."),
            _ => HealthReasons.Degraded(HealthReasons.OperatorPasswordWeak, "The password of the operator is too short: nobody can sign in."),
        });
}
