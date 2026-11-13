using System.Security.Cryptography;
using Microsoft.Extensions.Options;

namespace Geminarr.Core.Browser;

/// <summary>A live view that was asked for: the address to open and the single-use token that opens it, and until when.</summary>
public sealed record LiveViewTicket(string Id, string Token, DateTimeOffset ExpiresAt);

/// <summary>A viewer that is attached: it ends at the moment of its ticket, or when the operator closes it.</summary>
public sealed class LiveViewLease : IDisposable
{
    private readonly CancellationTokenSource _ends;

    internal LiveViewLease(string id, CancellationTokenSource ends)
    {
        Id = id;
        _ends = ends;
    }

    public string Id { get; }

    /// <summary>Cancelled when the live view ends: the time is up, the operator closed it, or the application stops.</summary>
    public CancellationToken Token => _ends.Token;

    public void Dispose() => _ends.Dispose();
}

/// <summary>The live view is already open, or waiting for its viewer.</summary>
public sealed class LiveViewBusyException : InvalidOperationException
{
    public LiveViewBusyException()
        : base("A live view is already open.")
    {
    }
}

/// <summary>
/// The live view of the browser (SPEC §7.3): **one at a time, ten minutes at most**. It is asked for by an authenticated call that gives a single-use token; the WebSocket that follows must present
/// it, and a second viewer is refused. A token that is not used within a minute is void. All in memory: a restart ends every live view, like every session.
/// </summary>
public sealed class LiveViewService
{
    /// <summary>How long a token waits for its viewer.</summary>
    public static readonly TimeSpan TokenLife = TimeSpan.FromMinutes(1);

    private readonly IOptions<BrowserOptions> _options;
    private readonly TimeProvider _clock;
    private readonly Lock _gate = new();
    private Pending? _pending;
    private Attached? _attached;

    public LiveViewService(IOptions<BrowserOptions> options, TimeProvider clock)
    {
        _options = options;
        _clock = clock;
    }

    /// <summary>Whether a live view is open or waits for its viewer.</summary>
    public bool Active
    {
        get
        {
            lock (_gate)
            {
                Expire();
                return _pending is not null || _attached is not null;
            }
        }
    }

    /// <summary>Asks for a live view: a ticket, or <see cref="LiveViewBusyException"/> when one is open already.</summary>
    public LiveViewTicket Start()
    {
        lock (_gate)
        {
            Expire();
            if (_pending is not null || _attached is not null)
            {
                throw new LiveViewBusyException();
            }

            var now = _clock.GetUtcNow();
            var ticket = new LiveViewTicket(Guid.NewGuid().ToString("N"), Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).Replace('+', '-').Replace('/', '_').TrimEnd('='), now + TimeSpan.FromMinutes(_options.Value.LiveViewMinutes));
            _pending = new Pending(ticket, now + TokenLife);
            return ticket;
        }
    }

    /// <summary>Opens the live view for the viewer that holds the token. The token is used up, whether it was right or not for a ticket that exists: it is not guessable twice.</summary>
    public LiveViewLease? TryAttach(string id, string token)
    {
        lock (_gate)
        {
            Expire();
            if (_pending is not { } pending || !string.Equals(pending.Ticket.Id, id, StringComparison.Ordinal))
            {
                return null;
            }

            _pending = null;
            if (!CryptographicOperations.FixedTimeEquals(System.Text.Encoding.UTF8.GetBytes(pending.Ticket.Token), System.Text.Encoding.UTF8.GetBytes(token)))
            {
                return null;
            }

            var ends = new CancellationTokenSource(pending.Ticket.ExpiresAt - _clock.GetUtcNow(), _clock);
            _attached = new Attached(pending.Ticket.Id, ends);
            return new LiveViewLease(pending.Ticket.Id, ends);
        }
    }

    /// <summary>Closes the live view that is open or waits (the operator's Close, or Disconnect). True when there was one.</summary>
    public bool Cancel()
    {
        lock (_gate)
        {
            var was = _pending is not null || _attached is not null;
            _pending = null;
            if (_attached is { } attached)
            {
                attached.Ends.Cancel();
                _attached = null;
            }

            return was;
        }
    }

    /// <summary>The viewer is gone: the live view is closed, and a new one may be asked for.</summary>
    public void Release(LiveViewLease lease)
    {
        lock (_gate)
        {
            if (_attached is { } attached && attached.Id == lease.Id)
            {
                _attached = null;
            }
        }
    }

    private void Expire()
    {
        if (_pending is { } pending && pending.TokenExpires <= _clock.GetUtcNow())
        {
            _pending = null;
        }
    }

    private sealed record Pending(LiveViewTicket Ticket, DateTimeOffset TokenExpires);

    private sealed record Attached(string Id, CancellationTokenSource Ends);
}
