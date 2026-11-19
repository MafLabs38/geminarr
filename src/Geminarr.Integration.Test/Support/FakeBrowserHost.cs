using Geminarr.Core.Browser;

namespace Geminarr.Integration.Test.Support;

/// <summary>A browser in memory for the tests of the host: its desktop answers, or it does not.</summary>
public sealed class FakeBrowserHost : IBrowserHost
{
    public bool Unreachable { get; set; }

    public BrowserState State() => new(Configured: true, Reachable: Unreachable ? false : null);

    public Task<bool> ProbeAsync(CancellationToken cancellationToken) => Task.FromResult(!Unreachable);
}
