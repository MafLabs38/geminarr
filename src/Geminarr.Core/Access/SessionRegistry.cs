using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;

namespace Geminarr.Core.Access;

/// <summary>
/// The sessions that are open, in memory: a cookie is valid only while its session is here. Signing out removes it, so that a copy of the cookie is
/// dead at once, and a session has an absolute lifetime. A restart ends every session (the keys that sign the cookies are in memory too), which is the
/// rule for a container that holds the session of someone's account.
/// </summary>
public sealed class SessionRegistry
{
    /// <summary>More sessions than one operator can have: a limit on what a flood of sign-ins could keep in memory.</summary>
    public const int MaxSessions = 100;

    private readonly IOptions<AuthOptions> _options;
    private readonly TimeProvider _clock;
    private readonly ConcurrentDictionary<string, DateTimeOffset> _sessions = new(StringComparer.Ordinal);
    private readonly Lock _startGate = new();

    public SessionRegistry(IOptions<AuthOptions> options, TimeProvider clock)
    {
        _options = options;
        _clock = clock;
    }

    public TimeSpan Lifetime => TimeSpan.FromHours(_options.Value.SessionHours);

    /// <summary>Starts a session and gives its identifier (the part of the cookie that nobody can guess).</summary>
    public string Start()
    {
        var id = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        lock (_startGate)
        {
            Prune();
            var excess = _sessions.Count - (MaxSessions - 1);
            foreach (var oldest in _sessions.OrderBy(pair => pair.Value).Take(Math.Max(excess, 0)).Select(pair => pair.Key).ToList())
            {
                _sessions.TryRemove(oldest, out _);
            }

            _sessions[id] = _clock.GetUtcNow() + Lifetime;
        }

        return id;
    }

    public bool IsActive(string? id) => id is not null && _sessions.TryGetValue(id, out var expires) && expires > _clock.GetUtcNow();

    public void End(string? id)
    {
        if (id is not null)
        {
            _sessions.TryRemove(id, out _);
        }
    }

    public int Count
    {
        get
        {
            Prune();
            return _sessions.Count;
        }
    }

    private void Prune()
    {
        var now = _clock.GetUtcNow();
        foreach (var pair in _sessions.Where(pair => pair.Value <= now))
        {
            _sessions.TryRemove(pair.Key, out _);
        }
    }
}
