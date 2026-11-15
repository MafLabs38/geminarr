using Geminarr.Core.Browser;
using Geminarr.Core.Connection;
using Geminarr.Core.Pipeline;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Geminarr.Core.Health;

/// <summary>
/// The attachment to the browser container (ADR 0016): healthy while the last attachment reached it (or none was needed), degraded with <c>browser-failed</c> when it could not be reached (the container is
/// not started, the debugging address is wrong, a limit killed it). Not a readiness check: a container without its browser is alive and says so.
/// </summary>
public sealed class BrowserHealthCheck : IHealthCheck
{
    private readonly IBrowserHost _browser;
    private readonly IOptions<ReaderOptions> _reader;
    private readonly IBrowserExposure _exposure;

    public BrowserHealthCheck(IBrowserHost browser, IOptions<ReaderOptions> reader, IBrowserExposure exposure)
    {
        _browser = browser;
        _reader = reader;
        _exposure = exposure;
    }

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (_reader.Value.Kind != ReaderKind.Web)
        {
            return Task.FromResult(HealthCheckResult.Healthy());
        }

        var state = _browser.State();
        if (!state.Configured)
        {
            return Task.FromResult(HealthReasons.Degraded(ConnectionReasons.NotConnected, "The reader is the browser but no browser is configured (GEMINARR__BROWSER__CDPURL)."));
        }

        if (_exposure.Check() is { IsExposed: true } exposure)
        {
            return Task.FromResult(HealthReasons.Degraded(ConnectionReasons.BrowserExposed, exposure.Describe()));
        }

        return Task.FromResult(
            state.Reachable == false
                ? HealthReasons.Degraded(ConnectionReasons.BrowserFailed, "The browser container could not be reached: is it started, and is the debugging address right?")
                : HealthCheckResult.Healthy());
    }
}
