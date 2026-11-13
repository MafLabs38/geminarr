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

    public ConnectionTracker(IConnectionStore store, IOptions<ReaderOptions> options, TimeProvider clock)
    {
        _store = store;
        _options = options;
        _clock = clock;
    }

    public ConnectionRecord Current() => _store.Get();

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

    private ConnectionRecord Move(ConnectionState state, string? reason, int failures, bool ok)
    {
        var current = _store.Get();
        var now = _clock.GetUtcNow();
        var next = new ConnectionRecord(state, state == current.State ? current.Since : now, ok ? now : current.LastOkAt, failures, reason);
        if (next != current)
        {
            _store.Save(next);
        }

        return next;
    }
}
