using Geminarr.Core.Browser;
using Geminarr.Core.Connection;
using Geminarr.Core.Pipeline;

namespace Geminarr.Integration.Test.Support;

/// <summary>A browser in memory for the tests of the host: what the attachment finds is what the test says.</summary>
public sealed class FakeBrowserHost : IBrowserHost
{
    public SessionCheck Check { get; set; } = SessionCheck.SignedIn;

    public bool Unreachable { get; set; }

    public int Disconnected { get; private set; }

    public AccountIdentity? Account { get; set; }

    public bool SigningIn { get; set; }

    public BrowserState State() => new(Configured: true, Reachable: Unreachable ? false : null, Account);

    public int SignInPagesOpened { get; private set; }

    public Task OpenSignInPageAsync(CancellationToken cancellationToken)
    {
        SignInPagesOpened++;
        return Unreachable ? throw new ReaderException(ConnectionReasons.BrowserFailed, "The browser container could not be reached.") : Task.CompletedTask;
    }

    public Task<bool> IsSigningInAsync(CancellationToken cancellationToken) => Task.FromResult(SigningIn);

    public Task<SessionCheck> CheckSessionAsync(CancellationToken cancellationToken) =>
        Unreachable ? throw new ReaderException(ConnectionReasons.BrowserFailed, "The browser container could not be reached.") : Task.FromResult(Check);

    public Task DisconnectAsync(CancellationToken cancellationToken)
    {
        Disconnected++;
        return Unreachable ? throw new ReaderException(ConnectionReasons.BrowserFailed, "The browser container could not be reached.") : Task.CompletedTask;
    }
}
