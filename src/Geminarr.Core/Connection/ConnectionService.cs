using Geminarr.Core.Browser;
using Geminarr.Core.Pipeline;
using Microsoft.Extensions.Options;

namespace Geminarr.Core.Connection;

/// <summary>
/// What the operator may ask about the connection: pause, resume, disconnect. A person signs in to the browser by hand, in its own page (ADR 0016): nothing here signs in, and nothing types a credential.
/// </summary>
public sealed class ConnectionService
{
    private readonly ConnectionTracker _tracker;
    private readonly IBrowserHost _browser;
    private readonly IOptions<ReaderOptions> _reader;
    private readonly TimeProvider _clock;
    private readonly SignInObserver _signIn;

    public ConnectionService(ConnectionTracker tracker, IBrowserHost browser, IOptions<ReaderOptions> reader, TimeProvider clock, SignInObserver signIn)
    {
        _tracker = tracker;
        _browser = browser;
        _reader = reader;
        _clock = clock;
        _signIn = signIn;
    }

    /// <summary>Whether a browser reads this Geminarr: without it there is nothing to pause or to disconnect.</summary>
    public bool HasBrowser => _reader.Value.Kind == ReaderKind.Web;

    public ConnectionRecord Current() => _tracker.Current();

    /// <summary>How long "signing in" is still said after the sign-in page is gone, while the check that follows looks at the session (unless it already found it good).</summary>
    private static readonly TimeSpan Settling = TimeSpan.FromSeconds(15);

    /// <summary>Whether a person is signing in at this moment (a tab of the browser is on a sign-in page), or has just finished and the session is being looked at.</summary>
    public bool SigningIn => _signIn.SigningIn || (_signIn.EndedWithin(Settling) && Current().State != ConnectionState.Connected);

    /// <summary>Opens the sign-in page of the account provider in the browser, where the person signs in by hand. Nothing here types a credential.</summary>
    public Task OpenSignInAsync(CancellationToken cancellationToken) =>
        HasBrowser ? _browser.OpenSignInPageAsync(cancellationToken) : throw new ConnectionRefusedException("No browser reads this Geminarr: there is nothing to sign in to.");

    /// <summary>Who the browser is signed in as, when the last check found out (the operator's to see, never logged).</summary>
    public AccountIdentity? Account => _browser.State().Account;

    /// <summary>Clears the session that the browser holds and forgets the connection: the person signs in again by hand. What waits for Mnemarr stays. A browser that cannot be reached is said, and the connection is forgotten all the same.</summary>
    public async Task DisconnectAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _browser.DisconnectAsync(cancellationToken);
        }
        finally
        {
            _tracker.Disconnected();
        }
    }

    /// <summary>Quiet until a moment, or until the operator resumes.</summary>
    public void Pause(TimeSpan? duration)
    {
        if (!HasBrowser)
        {
            throw new ConnectionRefusedException("No browser reads this Geminarr: there is nothing to pause.");
        }

        _tracker.Paused(duration is { } span ? _clock.GetUtcNow() + span : null);
    }

    public void Resume() => _tracker.ResumedIfPaused();
}

/// <summary>What the operator asked cannot be done now (no browser): a sentence of ours, said as it is. Any other error is not echoed.</summary>
public sealed class ConnectionRefusedException : InvalidOperationException
{
    public ConnectionRefusedException(string message)
        : base(message)
    {
    }
}
