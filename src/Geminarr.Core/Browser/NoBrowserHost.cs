namespace Geminarr.Core.Browser;

/// <summary>The browser of a host that has none (a test, a deployment without one): nothing is configured, nothing to look at.</summary>
public sealed class NoBrowserHost : IBrowserHost
{
    public BrowserState State() => new(Configured: false, Reachable: null);

    public Task<bool> ProbeAsync(CancellationToken cancellationToken) => Task.FromResult(true);
}
