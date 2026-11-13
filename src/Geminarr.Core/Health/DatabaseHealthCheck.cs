using Geminarr.Core.Storage;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Geminarr.Core.Health;

/// <summary>
/// Readiness: the database opens and is of a version this code knows. Without it nothing can be kept, so it is a check of readiness; the description never holds a path and the exception
/// stays on the result for the operator's logs, not for the caller.
/// </summary>
public sealed class DatabaseHealthCheck : IHealthCheck
{
    private readonly IDatabase _database;

    public DatabaseHealthCheck(IDatabase database)
    {
        _database = database;
    }

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = _database.Open();
            return Task.FromResult(HealthCheckResult.Healthy());
        }
        catch (UnsupportedSchemaException ex)
        {
            return Task.FromResult(HealthReasons.Unhealthy(HealthReasons.DatabaseStepUnknown, "The database was written by a newer version: it is not opened.", ex));
        }
        catch (Exception ex) when (StorageErrors.IsUnavailable(ex))
        {
            return Task.FromResult(HealthReasons.Unhealthy(HealthReasons.DatabaseUnreadable, "The database cannot be opened.", ex));
        }
    }
}
