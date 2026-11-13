using Geminarr.Core.Pipeline;

namespace Geminarr.Core.Browser;

/// <summary>The browser of a host that has none (a test, a build without the browser project): there is no profile, nothing to check, and no live view.</summary>
public sealed class NoBrowserHost : IBrowserHost
{
    public ProfileState Profile() => new(Exists: false, HasSession: false, Private: true);

    public void MarkSignedIn()
    {
    }

    public Task<SessionCheck> CheckSessionAsync(CancellationToken cancellationToken) => Task.FromResult(SessionCheck.NoProfile);

    public Task<ILiveView> OpenLiveViewAsync(CancellationToken cancellationToken) => throw new NotSupportedException("This host has no browser.");

    public Task DestroyProfileAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
