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

    /// <summary>No reader is configured: nothing is read.</summary>
    public ConnectionRecord NotConnected() => Move(ConnectionState.NotConnected, ConnectionReasons.NotConnected, failures: 0, ok: false);

    /// <summary>The session is good and the pass read what it came for.</summary>
    public ConnectionRecord Connected() => Move(ConnectionState.Connected, reason: null, failures: 0, ok: true);

    /// <summary>The pass found nobody signed in. The state turns <c>needs-login</c> when it happened enough times in a row; before that it stays what it was.</summary>
    public ConnectionRecord SignedOut()
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
    }

    /// <summary>The pass could not read: the reader says why (a code of <see cref="ConnectionReasons"/>).</summary>
    public ConnectionRecord Failed(string reason) => Move(ConnectionState.Degraded, reason, _store.Get().ConsecutiveFailures + 1, ok: false);

    /// <summary>A person is signing in through the live view: nothing is read meanwhile.</summary>
    public ConnectionRecord Connecting() => Move(ConnectionState.Connecting, ConnectionReasons.Connecting, failures: 0, ok: false);

    /// <summary>The live view ended without a session: the state is what it was before a person tried, as far as the profile says.</summary>
    public ConnectionRecord SignInEnded() =>
        _browser.Profile().HasSession ? Move(ConnectionState.NeedsLogin, ConnectionReasons.NeedsLogin, _store.Get().ConsecutiveFailures, ok: false) : NotConnected();

    /// <summary>The operator asked for quiet, until a moment or until they resume.</summary>
    public ConnectionRecord Paused(DateTimeOffset? until) => Move(ConnectionState.Paused, ConnectionReasons.Paused, failures: 0, ok: false, until);

    /// <summary>Quiet is over: connected again if the session was good before, and the next pass says the rest.</summary>
    public ConnectionRecord Resumed() =>
        _store.Get().LastOkAt is null ? NotConnected() : Move(ConnectionState.Connected, reason: null, failures: 0, ok: false);

    private ConnectionRecord Move(ConnectionState state, string? reason, int failures, bool ok, DateTimeOffset? pausedUntil = null)
    {
        var current = _store.Get();
        var now = _clock.GetUtcNow();
        var next = new ConnectionRecord(state, state == current.State ? current.Since : now, ok ? now : current.LastOkAt, failures, reason, pausedUntil);
        if (next != current)
        {
            _store.Save(next);
        }

        return next;
    }
}
