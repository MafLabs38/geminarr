using Geminarr.Core.Pipeline;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Geminarr.Core.Health;

/// <summary>What waits for Mnemarr: healthy while it moves, degraded when something has waited for more than an hour.</summary>
public sealed class OutboxHealthCheck : IHealthCheck
{
    /// <summary>How long a batch may wait before the outbox is said to grow.</summary>
    public static readonly TimeSpan GrowingAfter = TimeSpan.FromHours(1);

    private readonly IOutboxStore _outbox;
    private readonly TimeProvider _clock;

    public OutboxHealthCheck(IOutboxStore outbox, TimeProvider clock)
    {
        _outbox = outbox;
        _clock = clock;
    }

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            return Task.FromResult(_outbox.OldestAt() is { } oldest && _clock.GetUtcNow() - oldest > GrowingAfter
                ? HealthReasons.Degraded(HealthReasons.OutboxGrowing, "Something has waited for Mnemarr for more than an hour.")
                : HealthCheckResult.Healthy());
        }
        catch (Exception ex) when (Storage.StorageErrors.IsUnavailable(ex))
        {
            return Task.FromResult(HealthReasons.Degraded(HealthReasons.DatabaseUnreadable, "The outbox cannot be read."));
        }
    }
}
