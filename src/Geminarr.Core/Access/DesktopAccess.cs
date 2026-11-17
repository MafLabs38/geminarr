using System.Collections.Concurrent;

namespace Geminarr.Core.Access;

/// <summary>
/// How the operator gets to the web desktop of the browser (ADR 0019). The desktop is another program's, so it is served on an origin of its own, where the operator's session cookie is never sent: the operator's
/// session asks for a <b>ticket</b> (single use, a minute), the desktop's origin exchanges it for a cookie that belongs to that origin only, and the cookie is worth the operator's session behind it. Nothing of the desktop
/// can then act as the operator on Geminarr's own origin, and there is no second password to type. In memory, like the sessions: a restart ends them.
/// </summary>
public sealed class DesktopAccess
{
    /// <summary>How long a ticket may wait to be used: it is exchanged by the page that the person has just opened.</summary>
    public static readonly TimeSpan TicketLifetime = TimeSpan.FromMinutes(1);

    /// <summary>More tickets and desktop sessions than one operator can have: a limit on what a flood could keep in memory.</summary>
    public const int MaxEntries = 100;

    private readonly SessionRegistry _sessions;
    private readonly TimeProvider _clock;
    private readonly ConcurrentDictionary<string, (string Session, DateTimeOffset Expires)> _tickets = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, (string Session, DateTimeOffset Created)> _desktops = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();

    public DesktopAccess(SessionRegistry sessions, TimeProvider clock)
    {
        _sessions = sessions;
        _clock = clock;
    }

    /// <summary>A ticket for the operator whose session this is, or nothing when the session is not one that is open.</summary>
    public string? IssueTicket(string? sessionId)
    {
        if (!_sessions.IsActive(sessionId))
        {
            return null;
        }

        var ticket = SecureToken.New();
        lock (_gate)
        {
            var now = _clock.GetUtcNow();
            foreach (var stale in _tickets.Where(pair => pair.Value.Expires <= now).Select(pair => pair.Key).ToList())
            {
                _tickets.TryRemove(stale, out _);
            }

            foreach (var oldest in _tickets.OrderBy(pair => pair.Value.Expires).Take(Math.Max(_tickets.Count - (MaxEntries - 1), 0)).Select(pair => pair.Key).ToList())
            {
                _tickets.TryRemove(oldest, out _);
            }

            _tickets[ticket] = (sessionId!, now + TicketLifetime);
        }

        return ticket;
    }

    /// <summary>Exchanges a ticket for the secret of the desktop's cookie. A ticket is used once, whatever comes of it: a second use, an expired ticket and a ticket of an ended session give nothing.</summary>
    public string? Redeem(string? ticket)
    {
        if (ticket is null || !_tickets.TryRemove(ticket, out var entry))
        {
            return null;
        }

        var now = _clock.GetUtcNow();
        if (entry.Expires <= now || !_sessions.IsActive(entry.Session))
        {
            return null;
        }

        var token = SecureToken.New();
        lock (_gate)
        {
            foreach (var gone in _desktops.Where(pair => !_sessions.IsActive(pair.Value.Session)).Select(pair => pair.Key).ToList())
            {
                _desktops.TryRemove(gone, out _);
            }

            foreach (var oldest in _desktops.OrderBy(pair => pair.Value.Created).Take(Math.Max(_desktops.Count - (MaxEntries - 1), 0)).Select(pair => pair.Key).ToList())
            {
                _desktops.TryRemove(oldest, out _);
            }

            _desktops[token] = (entry.Session, now);
        }

        return token;
    }

    /// <summary>The operator's session that a desktop cookie stands for, while that session is open: signing out, or the end of its lifetime, ends the desktop's too.</summary>
    public string? SessionOf(string? token) =>
        token is not null && _desktops.TryGetValue(token, out var entry) && _sessions.IsActive(entry.Session) ? entry.Session : null;
}
