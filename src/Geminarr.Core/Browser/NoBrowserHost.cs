using Geminarr.Core.Pipeline;

namespace Geminarr.Core.Browser;

/// <summary>The browser of a host that has none (a test, a deployment without one): nothing is configured, nothing to check, nothing to clear.</summary>
public sealed class NoBrowserHost : IBrowserHost
{
    public BrowserState State() => new(Configured: false, Reachable: null);

    public Task<SessionCheck> CheckSessionAsync(CancellationToken cancellationToken) => Task.FromResult(SessionCheck.NoProfile);

    public Task DisconnectAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
