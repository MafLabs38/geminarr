using Geminarr.Core.Health;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Geminarr.Host.Health;

public static class HealthExtensions
{
    public const string LivePath = "/health/live";
    public const string ReadyPath = "/health/ready";

    private const string ReadyTag = "ready";

    /// <summary>Whether a path is one of the two probes of the platform (and nothing else under <c>/health</c>).</summary>
    public static bool IsProbe(PathString path) =>
        path.Equals(LivePath, StringComparison.OrdinalIgnoreCase) || path.Equals(ReadyPath, StringComparison.OrdinalIgnoreCase);

    public static IServiceCollection AddGeminarrHealthChecks(this IServiceCollection services)
    {
        // One instance: it keeps its last result, and AddCheck would otherwise build a new one for every probe.
        services.TryAddSingleton<DataFolderHealthCheck>();
        services.AddHealthChecks()
            .AddCheck<DataFolderHealthCheck>("data-folder", tags: [ReadyTag])
            .AddCheck<DatabaseHealthCheck>("database", tags: [ReadyTag])
            // Not a readiness check: a container without an operator is alive and closed, and the platform must not restart it for that.
            .AddCheck<OperatorHealthCheck>("operator")
            .AddCheck<ConnectionHealthCheck>("connection")
            // The profile of the browser: private to its owner and readable (the session is the crown jewel).
            .AddCheck<ProfileHealthCheck>("profile")
            .AddCheck<MnemarrHealthCheck>("mnemarr")
            .AddCheck<OutboxHealthCheck>("outbox");
        return services;
    }

    /// <summary>
    /// <c>/health/live</c>: the process answers (no check, for a liveness probe).
    /// <c>/health/ready</c>: it can also do its job (the data folder is writable and the database opens, for a readiness probe).
    /// </summary>
    public static IEndpointRouteBuilder MapGeminarrHealthChecks(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapHealthChecks(LivePath, new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
        endpoints.MapHealthChecks(ReadyPath, new HealthCheckOptions { Predicate = registration => registration.Tags.Contains(ReadyTag) }).AllowAnonymous();
        return endpoints;
    }
}
