using Geminarr.Core.Pipeline;
using Microsoft.Extensions.Options;

namespace Geminarr.Core.Connection;

/// <summary>
/// Keeps the state of the connection as the passes find it (SPEC §7.2): connected when the session is good, <c>needs-login</c> only after several passes in a row found nobody signed
/// in (a network error is never "not signed in"), degraded when a pass failed for another reason. A pause takes the state over and gives it back. A person signs in to the browser by hand (ADR 0016): the next pass finds it.
/// </summary>
public sealed class ConnectionTracker
{
    private readonly IConnectionStore _store;
    private readonly IOptions<ReaderOptions> _options;
    private readonly TimeProvider _clock;
    private readonly Lock _gate = new();
    private readonly AsyncLocal<long?> _passEpoch = new();
    private long _epoch;

    public ConnectionTracker(IConnectionStore store, IOptions<ReaderOptions> options, TimeProvider clock)
    {
        _store = store;
        _options = options;
        _clock = clock;
    }

    /// <summary>The state as it is now: a read, which writes nothing. A pause whose moment has come has ended by itself, and what is read is what the resume will give; it is written by <see cref="Settle"/> (the passes do) or by the next move.</summary>
    public ConnectionRecord Current() => Atomically(() =>
    {
        var record = _store.Get();
        return IsOver(record) ? ResumedRecord(record) : record;
    });

    /// <summary>Writes the end of a pause whose moment has come (a pass begins by it); anything else is left as it is.</summary>
    public ConnectionRecord Settle() => Atomically(() =>
    {
        var record = _store.Get();
        return IsOver(record) ? Resumed() : record;
    });

    private bool IsOver(ConnectionRecord record) => record.State == ConnectionState.Paused && record.PausedUntil is { } until && until <= _clock.GetUtcNow();

    /// <summary>A pass says what it found as of this moment: a disconnect that comes later makes it moot. Dispose ends the scope.</summary>
    public IDisposable BeginPass()
    {
        lock (_gate)
        {
            _passEpoch.Value = _epoch;
        }

        return new PassScope(_passEpoch);
    }

    private sealed class PassScope : IDisposable
    {
        private readonly AsyncLocal<long?> _epoch;

        public PassScope(AsyncLocal<long?> epoch)
        {
            _epoch = epoch;
        }

        public void Dispose() => _epoch.Value = null;
    }

    /// <summary>No reader is configured, or the pass found no browser configured: nothing is read. A person who is signing in is not interrupted by a pass.</summary>
    public ConnectionRecord NotConnected() => Atomically(() => WhenNotPaused(() => Move(ConnectionState.NotConnected, _store.Get().LastReason == ConnectionReasons.Disconnected ? ConnectionReasons.Disconnected : ConnectionReasons.NotConnected, failures: 0, ok: false)));

    /// <summary>The operator disconnected: the session is being signed out, whatever else was going on. The record says so (<see cref="ConnectionReasons.Disconnected"/>) until somebody connects again.</summary>
    public ConnectionRecord Disconnected() => Atomically(() =>
    {
        _epoch++;
        var current = _store.Get();
        var record = new ConnectionRecord(ConnectionState.NotConnected, current.State == ConnectionState.NotConnected ? current.Since : _clock.GetUtcNow(), LastOkAt: null, 0, ConnectionReasons.Disconnected);
        if (record != current)
        {
            _store.Save(record);
        }

        return record;
    });

    /// <summary>The operator gave a pending sign-out up (by pressing *Sign in*): the record no longer says that a sign-out is asked, so that a restart does not ask for it again.</summary>
    public ConnectionRecord GaveUpTheDisconnect() => Atomically(() =>
    {
        var current = _store.Get();
        if (current.LastReason != ConnectionReasons.Disconnected)
        {
            return current;
        }

        var record = current with { LastReason = ConnectionReasons.NotConnected };
        _store.Save(record);
        return record;
    });

    /// <summary>The session is good and the pass read what it came for.</summary>
    public ConnectionRecord Connected() => Atomically(() => WhenNotPaused(() => Move(ConnectionState.Connected, reason: null, failures: 0, ok: true)));

    /// <summary>The pass found nobody signed in. The state turns <c>needs-login</c> when it happened enough times in a row; before that it stays what it was.</summary>
    public ConnectionRecord SignedOut() => Atomically(() => WhenNotPaused(() =>
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
    }));

    /// <summary>The pass could not read: the reader says why (a code of <see cref="ConnectionReasons"/>).</summary>
    public ConnectionRecord Failed(string reason) => Atomically(() => WhenNotPaused(() => Move(ConnectionState.Degraded, reason, _store.Get().ConsecutiveFailures + 1, ok: false)));

    /// <summary>The operator asked for quiet, until a moment or until they resume. What was known of the session is kept to go back to.</summary>
    public ConnectionRecord Paused(DateTimeOffset? until) => Atomically(() => Move(ConnectionState.Paused, ConnectionReasons.Paused, failures: 0, ok: false, until, BeforeTakingOver()));

    /// <summary>Quiet is over: the state is what it was before, and the next pass says the rest.</summary>
    public ConnectionRecord Resumed() => Atomically(() =>
    {
        var current = _store.Get();
        return Save(ResumedRecord(current), current);
    });

    /// <summary>The operator resumes: only what is paused is resumed (a pause that ended by itself included), and the reading and the move are one step (a pause that ended just before is not resumed a second time).</summary>
    public ConnectionRecord ResumedIfPaused() => Atomically(() =>
    {
        var current = _store.Get();
        return current.State == ConnectionState.Paused ? Save(ResumedRecord(current), current) : current;
    });

    /// <summary>What the resume gives, from a record: what the connection was before the pause, or, when nothing worked before it, not connected; otherwise it was good and the next pass says so.</summary>
    private ConnectionRecord ResumedRecord(ConnectionRecord current)
    {
        if (current.Before is { State: not (ConnectionState.NotConnected or ConnectionState.Paused) } before)
        {
            return Build(current, before.State, before.Reason, before.Failures, ok: false);
        }

        // A disconnect that the extension has not confirmed is kept through a pause: a restart after the resume must still ask for the sign-out.
        var reason = current.Before is { Reason: ConnectionReasons.Disconnected } ? ConnectionReasons.Disconnected : ConnectionReasons.NotConnected;
        return current.Before is { State: ConnectionState.NotConnected } || current.LastOkAt is null
            ? Build(current, ConnectionState.NotConnected, reason, failures: 0, ok: false)
            : Build(current, ConnectionState.Connected, reason: null, failures: 0, ok: false);
    }

    /// <summary>
    /// A move reads the record and writes it: two at once (a pass, a call of the operator, the page reading the state that settles) would each write over what the other decided. One at a time, in this process,
    /// which is the only one that writes (the lock is re-entrant: a move may call another).
    /// </summary>
    private ConnectionRecord Atomically(Func<ConnectionRecord> move)
    {
        lock (_gate)
        {
            return move();
        }
    }

    /// <summary>A pass says what it found only when the passes are not paused: a pause that was asked for while a pass was reading is not undone by the pass that ends.</summary>
    private ConnectionRecord WhenNotPaused(Func<ConnectionRecord> move)
    {
        var current = _store.Get();
        // A pass that began before the operator disconnected says what it found about a profile that is gone: it is not told to anybody.
        if (_passEpoch.Value is { } began && began != _epoch)
        {
            return current;
        }

        var paused = current.State == ConnectionState.Paused && (current.PausedUntil is null || current.PausedUntil > _clock.GetUtcNow());
        return paused ? current : move();
    }

    private ConnectionBefore? BeforeTakingOver()
    {
        var current = _store.Get();
        return current.State == ConnectionState.Paused ? current.Before : new ConnectionBefore(current.State, current.LastReason, current.ConsecutiveFailures);
    }

    private ConnectionRecord Move(ConnectionState state, string? reason, int failures, bool ok, DateTimeOffset? pausedUntil = null, ConnectionBefore? before = null)
    {
        var current = _store.Get();
        return Save(Build(current, state, reason, failures, ok, pausedUntil, before), current);
    }

    /// <summary>The record that a move would write, from the one that is there: pure, so that a read can say what a move would give without giving it.</summary>
    private ConnectionRecord Build(ConnectionRecord current, ConnectionState state, string? reason, int failures, bool ok, DateTimeOffset? pausedUntil = null, ConnectionBefore? before = null)
    {
        var now = _clock.GetUtcNow();
        return new ConnectionRecord(state, state == current.State ? current.Since : now, ok ? now : current.LastOkAt, failures, reason, pausedUntil, before);
    }

    /// <summary>Writes the record when it differs from <paramref name="current"/>, which the caller has just read under the same lock: a move reads the record once and hands it here.</summary>
    private ConnectionRecord Save(ConnectionRecord next, ConnectionRecord current)
    {
        if (next != current)
        {
            _store.Save(next);
        }

        return next;
    }
}
