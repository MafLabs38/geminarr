using Geminarr.Core.Browser;
using Geminarr.Core.Connection;
using Geminarr.Core.Pipeline;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Geminarr.Core.Health;

/// <summary>
/// The attachment to the browser container (ADR 0016): healthy while the last attachment reached it (or none was needed), degraded with <c>browser-failed</c> when it could not be reached (the container is
/// not started, its desktop address is wrong, a limit killed it). Not a readiness check: a container without its browser is alive and says so.
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

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        // Whatever the reader is: a browser container that something else could reach is the most serious state there is (it says nothing when no browser is configured).
        if (_exposure.Check() is { IsExposed: true } exposure)
        {
            return HealthReasons.Degraded(exposure.Reason, exposure.Describe());
        }

        if (_reader.Value.Kind != ReaderKind.Web)
        {
            return HealthCheckResult.Healthy();
        }

        var state = _browser.State();
        if (!state.Configured)
        {
            return HealthReasons.Degraded(ConnectionReasons.NotConnected, "The reader is the browser but no browser is configured (GEMINARR__BROWSER__DESKTOPURL).");
        }

        return await _browser.ProbeAsync(cancellationToken)
            ? HealthCheckResult.Healthy()
            : HealthReasons.Degraded(ConnectionReasons.BrowserFailed, "The browser container could not be reached: is it started, and is its desktop address right?");
    }
}
