using Geminarr.Core.Browser;
using Geminarr.Core.Pipeline;
using Microsoft.Extensions.Options;

namespace Geminarr.Core.Connection;

/// <summary>
/// Keeps the state of the connection as the passes find it (SPEC §7.2): connected when the session is good, <c>needs-login</c> only after several passes in a row found nobody signed
/// in (a network error is never "not signed in"), degraded when a pass failed for another reason. The live view, pausing and disconnecting come with the next vertical.
/// </summary>
public sealed class ConnectionTracker
{
    private readonly IConnectionStore _store;
    private readonly IOptions<ReaderOptions> _options;
    private readonly TimeProvider _clock;
    private readonly LiveViewService _views;
    private readonly IBrowserHost _browser;

    public ConnectionTracker(IConnectionStore store, IOptions<ReaderOptions> options, TimeProvider clock, LiveViewService views, IBrowserHost browser)
    {
        _store = store;
        _options = options;
        _clock = clock;
        _views = views;
        _browser = browser;
    }

    /// <summary>
    /// The state as it is now. Two things end by themselves and are settled here, where everybody reads the state: a live view that nobody opened or that has ended (the state goes back to what the
    /// profile says), and a pause whose moment has come (the passes resume).
    /// </summary>
    public ConnectionRecord Current()
    {
        var record = _store.Get();
        if (record.State == ConnectionState.Connecting && !_views.Active)
        {
            return SignInEnded();
        }

        return record.State == ConnectionState.Paused && record.PausedUntil is { } until && until <= _clock.GetUtcNow() ? Resumed() : record;
    }

    /// <summary>No reader is configured, or the pass found no profile: nothing is read. A person who is signing in is not interrupted by a pass.</summary>
    public ConnectionRecord NotConnected() => WhenNobodyIsSigningIn(() => Move(ConnectionState.NotConnected, ConnectionReasons.NotConnected, failures: 0, ok: false));

    /// <summary>The operator disconnected: the profile is gone, whatever else was going on.</summary>
    public ConnectionRecord Disconnected() => Move(ConnectionState.NotConnected, ConnectionReasons.NotConnected, failures: 0, ok: false);

    /// <summary>The session is good and the pass read what it came for.</summary>
    public ConnectionRecord Connected() => WhenNobodyIsSigningIn(() => Move(ConnectionState.Connected, reason: null, failures: 0, ok: true));

    /// <summary>A person signed in through the live view: the session is good.</summary>
    public ConnectionRecord SignedIn() => Move(ConnectionState.Connected, reason: null, failures: 0, ok: true);

    /// <summary>The pass found nobody signed in. The state turns <c>needs-login</c> when it happened enough times in a row; before that it stays what it was.</summary>
    public ConnectionRecord SignedOut() => WhenNobodyIsSigningIn(() =>
    {
        var current = _store.Get();
        // A streak of signed-out passes is its own: failures of another kind (the network, a limit, a page that changed) are not people being signed out and do not count towards it.
        var afterAnotherFailure = current.LastReason is not (null or ConnectionReasons.NeedsLogin);
        var failures = afterAnotherFailure ? 1 : current.ConsecutiveFailures + 1;
        if (failures >= _options.Value.LoginFailures)
        {
            return Move(ConnectionState.NeedsLogin, ConnectionReasons.NeedsLogin, failures, ok: false);
        }

        // Not yet: the state stays what it was (a connection that was never made, or one that failed otherwise, is now one that is in doubt for this reason).
        if (afterAnotherFailure)
        {
            return Move(ConnectionState.Degraded, ConnectionReasons.NeedsLogin, failures, ok: false);
        }

        return Move(current.State, current.LastReason, failures, ok: false);
    });

    /// <summary>The pass could not read: the reader says why (a code of <see cref="ConnectionReasons"/>).</summary>
    public ConnectionRecord Failed(string reason) => WhenNobodyIsSigningIn(() => Move(ConnectionState.Degraded, reason, _store.Get().ConsecutiveFailures + 1, ok: false));

    /// <summary>A person is signing in through the live view: nothing is read meanwhile, and what was known of the session is kept to go back to.</summary>
    public ConnectionRecord Connecting() => Move(ConnectionState.Connecting, ConnectionReasons.Connecting, failures: 0, ok: false, before: BeforeTakingOver());

    /// <summary>The live view ended without a session: the state is what it was before a person tried; with nothing to go back to, what the profile says.</summary>
    public ConnectionRecord SignInEnded() =>
        BackToWhatItWas() ?? (_browser.Profile().HasSession ? Move(ConnectionState.NeedsLogin, ConnectionReasons.NeedsLogin, _store.Get().ConsecutiveFailures, ok: false) : Disconnected());

    /// <summary>The operator asked for quiet, until a moment or until they resume. What was known of the session is kept to go back to.</summary>
    public ConnectionRecord Paused(DateTimeOffset? until) => Move(ConnectionState.Paused, ConnectionReasons.Paused, failures: 0, ok: false, until, BeforeTakingOver());

    /// <summary>Quiet is over: the state is what it was before, and the next pass says the rest.</summary>
    public ConnectionRecord Resumed() => BackToWhatItWas() ?? (_store.Get().LastOkAt is null ? Disconnected() : Move(ConnectionState.Connected, reason: null, failures: 0, ok: false));

    /// <summary>A pass says what it found only when nobody is signing in: the live view's moves are the only ones that count then.</summary>
    private ConnectionRecord WhenNobodyIsSigningIn(Func<ConnectionRecord> move)
    {
        var current = _store.Get();
        return current.State == ConnectionState.Connecting && _views.Active ? current : move();
    }

    private ConnectionBefore? BeforeTakingOver()
    {
        var current = _store.Get();
        return current.State is ConnectionState.Connecting or ConnectionState.Paused ? current.Before : new ConnectionBefore(current.State, current.LastReason, current.ConsecutiveFailures);
    }

    private ConnectionRecord? BackToWhatItWas()
    {
        var current = _store.Get();
        return current.Before is { State: not (ConnectionState.NotConnected or ConnectionState.Connecting or ConnectionState.Paused) } before
            ? Move(before.State, before.Reason, before.Failures, ok: false)
            : null;
    }

    private ConnectionRecord Move(ConnectionState state, string? reason, int failures, bool ok, DateTimeOffset? pausedUntil = null, ConnectionBefore? before = null)
    {
        var current = _store.Get();
        var now = _clock.GetUtcNow();
        var next = new ConnectionRecord(state, state == current.State ? current.Since : now, ok ? now : current.LastOkAt, failures, reason, pausedUntil, before);
        if (next != current)
        {
            _store.Save(next);
        }

        return next;
    }
}
