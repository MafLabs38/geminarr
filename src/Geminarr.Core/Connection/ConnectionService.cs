using Geminarr.Core.Browser;
using Geminarr.Core.Pipeline;
using Microsoft.Extensions.Options;

namespace Geminarr.Core.Connection;

/// <summary>
/// What the operator may ask about the connection: pause, resume, disconnect. A person signs in to the browser by hand, in its own page (ADR 0016): nothing here signs in, and nothing types a credential.
/// </summary>
public sealed class ConnectionService
{
    /// <summary>How long "signing in" is still said after the sign-in page is gone, while the check that follows looks at the session (unless it already found it good).</summary>
    private static readonly TimeSpan Settling = TimeSpan.FromSeconds(15);

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

    /// <summary>Whether a person is signing in at this moment (a tab of the browser is on a sign-in page), or has just finished and the session is being looked at. Not once the passes have said that a sign-in is needed again (a tab left on an account picker is not somebody typing).</summary>
    public bool SigningIn => SigningInAt(Current());

    /// <summary>The same, for a record that the caller has read (so that a response says one state, not two reads of it).</summary>
    public bool SigningInAt(ConnectionRecord record) => record.State != ConnectionState.NeedsLogin && (_signIn.SigningIn || (_signIn.EndedWithin(Settling) && record.State != ConnectionState.Connected));

    /// <summary>Opens the sign-in page of the account provider in the browser, where the person signs in by hand. Nothing here types a credential.</summary>
    public Task OpenSignInAsync(CancellationToken cancellationToken) =>
        HasBrowser && _browser.State().Configured ? _browser.OpenSignInPageAsync(cancellationToken) : throw new ConnectionRefusedException("No browser reads this Geminarr: there is nothing to sign in to.");

    /// <summary>Who the browser is signed in as, when the last check found out (the operator's to see, never logged); nobody while the connection is not one.</summary>
    public AccountIdentity? Account => AccountAt(Current());

    /// <summary>The same, for a record that the caller has read.</summary>
    public AccountIdentity? AccountAt(ConnectionRecord record) => record.State is ConnectionState.NotConnected or ConnectionState.NeedsLogin ? null : _browser.State().Account;

    /// <summary>
    /// Clears the session that the browser holds and forgets the connection: the person signs in again by hand. What waits for Mnemarr stays. It is all or nothing: a browser that cannot be reached is said, and nothing changes
    /// (the session is still in it, so the connection and the account are still what they were, and the operator tries again when it is back).
    /// </summary>
    public async Task DisconnectAsync(CancellationToken cancellationToken)
    {
        await _browser.DisconnectAsync(cancellationToken);
        _tracker.Disconnected();
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
