namespace Geminarr.Core.Access;

/// <summary>
/// Locks a name out after repeated failures to sign in: five failed attempts within fifteen minutes lock it until they age out. An attempt is
/// counted <b>before</b> its password is checked and cleared when it succeeds, so that a burst of parallel attempts cannot all pass before the
/// fifth failure is recorded. It counts per name whether the operator has that name or not, so that a lockout tells nothing about which name is the
/// operator's, and it never tracks more than <see cref="MaxTracked"/> names: at the cap the name of the operator is never forgotten, and a name that
/// is not it is refused while no room can be made. In memory (one instance): a restart forgets, and that is the way to lift a lockout early.
/// </summary>
public sealed class LoginThrottle
{
    public const int MaxFailures = 5;
    public const int MaxTracked = 1_000;

    /// <summary>How long failures count, and so how long a lockout lasts at most.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    private readonly TimeProvider _clock;
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _gate = new();

    public LoginThrottle(TimeProvider clock)
    {
        _clock = clock;
    }

    /// <summary>Counts an attempt, unless the name is locked or there is no room for it. False means refused and not counted.</summary>
    public bool TryReserve(string name, bool isOperator)
    {
        lock (_gate)
        {
            if (!_entries.TryGetValue(name, out var entry))
            {
                if (_entries.Count >= MaxTracked && !MakeRoom())
                {
                    return false;
                }

                entry = new Entry(isOperator);
                _entries[name] = entry;
            }

            entry.IsOperator |= isOperator;
            Prune(entry.Attempts);
            if (entry.Attempts.Count >= MaxFailures)
            {
                return false;
            }

            entry.Attempts.Add(_clock.GetUtcNow());
            return true;
        }
    }

    /// <summary>The attempt succeeded: the name starts again from nothing.</summary>
    public void Succeeded(string name)
    {
        lock (_gate)
        {
            _entries.Remove(name);
        }
    }

    /// <summary>How long a locked name stays locked: until its oldest counted attempt ages out. Null when it is not locked.</summary>
    public TimeSpan? LockedFor(string name)
    {
        lock (_gate)
        {
            if (!_entries.TryGetValue(name, out var entry))
            {
                return null;
            }

            Prune(entry.Attempts);
            return entry.Attempts.Count >= MaxFailures ? entry.Attempts[0] + Window - _clock.GetUtcNow() : null;
        }
    }

    private void Prune(List<DateTimeOffset> attempts)
    {
        var limit = _clock.GetUtcNow() - Window;
        attempts.RemoveAll(at => at < limit);
    }

    /// <summary>At the cap: forget what has expired, then the tenth of the names that are not the operator's whose last attempt is the oldest.</summary>
    private bool MakeRoom()
    {
        foreach (var name in _entries.Keys.ToList())
        {
            Prune(_entries[name].Attempts);
            if (_entries[name].Attempts.Count == 0)
            {
                _entries.Remove(name);
            }
        }

        if (_entries.Count < MaxTracked)
        {
            return true;
        }

        var oldest = _entries.Where(pair => !pair.Value.IsOperator).OrderBy(pair => pair.Value.Attempts[^1]).Take(MaxTracked / 10).Select(pair => pair.Key).ToList();
        foreach (var name in oldest)
        {
            _entries.Remove(name);
        }

        return oldest.Count > 0;
    }

    private sealed class Entry
    {
        public Entry(bool isOperator)
        {
            IsOperator = isOperator;
        }

        public bool IsOperator { get; set; }

        public List<DateTimeOffset> Attempts { get; } = [];
    }
}
