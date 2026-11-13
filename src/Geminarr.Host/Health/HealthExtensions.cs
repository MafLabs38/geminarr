using Geminarr.Core.Health;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Geminarr.Host.Health;

public static class HealthExtensions
{
    private const string ReadyTag = "ready";

    public static IServiceCollection AddGeminarrHealthChecks(this IServiceCollection services)
    {
        // One instance: it keeps its last result, and AddCheck would otherwise build a new one for every probe.
        services.TryAddSingleton<DataFolderHealthCheck>();
        services.AddHealthChecks()
            .AddCheck<DataFolderHealthCheck>("data-folder", tags: [ReadyTag])
            // Not a readiness check: a container without an operator is alive and closed, and the platform must not restart it for that.
            .AddCheck<OperatorHealthCheck>("operator");
        return services;
    }

    /// <summary>
    /// <c>/health/live</c>: the process answers (no check, for a liveness probe).
    /// <c>/health/ready</c>: it can also do its job (the data folder is writable, for a readiness probe).
    /// </summary>
    public static IEndpointRouteBuilder MapGeminarrHealthChecks(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
        endpoints.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = registration => registration.Tags.Contains(ReadyTag) }).AllowAnonymous();
        return endpoints;
    }
}
