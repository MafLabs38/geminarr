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

    public bool DisconnectRefused { get; set; }

    public BrowserState State() => new(Configured: true, Reachable: Unreachable ? false : null);

    public Task<SessionCheck> CheckSessionAsync(CancellationToken cancellationToken) =>
        Unreachable ? throw new ReaderException(ConnectionReasons.BrowserFailed, "The browser container could not be reached.") : Task.FromResult(Check);

    public Task DisconnectAsync(CancellationToken cancellationToken)
    {
        Disconnected++;
        if (DisconnectRefused)
        {
            throw new Geminarr.Core.Connection.ConnectionRefusedException("This version cannot clear the session.");
        }

        return Unreachable ? throw new ReaderException(ConnectionReasons.BrowserFailed, "The browser container could not be reached.") : Task.CompletedTask;
    }
}
