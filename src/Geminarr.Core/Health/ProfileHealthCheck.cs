using Geminarr.Core.Browser;
using Geminarr.Core.Pipeline;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Geminarr.Core.Health;

/// <summary>The folder of the browser profile, where the signed-in session lives: there, readable, and private (D-09). Only a browser that reads makes it matter.</summary>
public sealed class ProfileHealthCheck : IHealthCheck
{
    private readonly IBrowserHost _browser;
    private readonly IOptions<ReaderOptions> _reader;

    public ProfileHealthCheck(IBrowserHost browser, IOptions<ReaderOptions> reader)
    {
        _browser = browser;
        _reader = reader;
    }

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (_reader.Value.Kind != ReaderKind.Web)
        {
            return Task.FromResult(HealthCheckResult.Healthy());
        }

        var profile = _browser.Profile();
        return Task.FromResult(profile switch
        {
            { Exists: false } => HealthReasons.Degraded(HealthReasons.ProfileMissing, "The folder of the browser profile does not exist: its volume is not mounted."),
            { Readable: false } => HealthReasons.Degraded(HealthReasons.ProfileUnreadable, "The folder of the browser profile cannot be read."),
            { Private: false } => HealthReasons.Degraded(HealthReasons.ProfileNotPrivate, "The folder of the browser profile can be read by others: it holds a signed-in session."),
            _ => HealthCheckResult.Healthy(),
        });
    }
}
