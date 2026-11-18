using Geminarr.Core.Browser;
using Geminarr.Core.Extension;
using Geminarr.Core.Pipeline;
using Microsoft.Extensions.Options;

namespace Geminarr.Core.Connection;

/// <summary>
/// What the operator may ask about the connection: pause, resume, disconnect. A person signs in to the browser by hand, in its own page (ADR 0016): nothing here signs in, and nothing types a credential.
/// </summary>
public sealed class ConnectionService
{
    private readonly ConnectionTracker _tracker;
    private readonly ExtensionInbox _inbox;
    private readonly IOptions<ReaderOptions> _reader;
    private readonly TimeProvider _clock;

    public ConnectionService(ConnectionTracker tracker, ExtensionInbox inbox, IOptions<ReaderOptions> reader, TimeProvider clock)
    {
        _tracker = tracker;
        _inbox = inbox;
        _reader = reader;
        _clock = clock;
    }

    /// <summary>Whether a browser reads this Geminarr: without it there is nothing to pause or to disconnect.</summary>
    public bool HasBrowser => _reader.Value.Kind == ReaderKind.Web;

    public ConnectionRecord Current() => _tracker.Current();

    /// <summary>The operator pressed *Sign in*: a person is about to sign in to the browser by hand, so the extension is asked for rounds until it says that somebody is.</summary>
    public void BeginSigningIn() => _inbox.BeginSigningIn(_clock.GetUtcNow());

    /// <summary>Whether a sign-in that the operator started is being waited for (and the session is not good yet).</summary>
    public bool SigningInAt(ConnectionRecord record) => record.State != ConnectionState.Connected && _inbox.SigningIn(_clock.GetUtcNow());

    /// <summary>Whether a disconnect has been asked and the extension has not yet said that nobody is signed in.</summary>
    public bool SignOutPending => _inbox.SignOutPending;

    /// <summary>Who the browser is signed in as, as the extension read it (the operator's to see, never logged); nobody while the connection is not one.</summary>
    public AccountIdentity? AccountAt(ConnectionRecord record) => record.State is ConnectionState.NotConnected or ConnectionState.NeedsLogin ? null : _inbox.Session?.Account;

    /// <summary>
    /// Signs the account out of the browser, by the extension (it deletes the cookies of its browser at its next word: no debugging port, no cookie read), and forgets the connection: the person signs in again by hand. What
    /// waits for Mnemarr stays. The request is asked until the extension confirms (the browser may be off), and the record says so, so that a restart does not forget it.
    /// </summary>
    public void Disconnect()
    {
        _inbox.RequestSignOut();
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
