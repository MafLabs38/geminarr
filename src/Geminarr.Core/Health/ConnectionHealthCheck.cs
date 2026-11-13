using Geminarr.Core.Connection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Geminarr.Core.Health;

/// <summary>The connection to the assistant, by its state (SPEC §7.2): healthy only when connected.</summary>
public sealed class ConnectionHealthCheck : IHealthCheck
{
    private readonly ConnectionTracker _connection;

    public ConnectionHealthCheck(ConnectionTracker connection)
    {
        _connection = connection;
    }

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var record = _connection.Current();
            return Task.FromResult(record.State switch
            {
                ConnectionState.Connected => HealthCheckResult.Healthy(),
                ConnectionState.NeedsLogin => HealthReasons.Degraded(ConnectionReasons.NeedsLogin, "The session has expired: sign in again."),
                ConnectionState.Degraded => HealthReasons.Degraded(record.LastReason ?? ConnectionReasons.Unreachable, "The last pass could not read the assistant."),
                _ => HealthReasons.Degraded(ConnectionReasons.NotConnected, "Not connected to an assistant: nothing is read."),
            });
        }
        catch (Exception ex) when (Storage.StorageErrors.IsUnavailable(ex))
        {
            return Task.FromResult(HealthReasons.Degraded(HealthReasons.DatabaseUnreadable, "The state of the connection cannot be read: the database cannot be reached."));
        }
    }
}
