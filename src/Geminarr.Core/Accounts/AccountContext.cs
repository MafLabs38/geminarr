using Geminarr.Core.Connection;
using Geminarr.Core.Pipeline;

namespace Geminarr.Core.Accounts;

/// <summary>
/// The account of the reader while somebody is signed in, else the one that was signed in last (kept in the root database), unless a piece of work has taken one (<see cref="IAccountContext.Use"/>), which follows its
/// awaits.
/// </summary>
public sealed class AccountContext : IAccountContext
{
    private static readonly AsyncLocal<Taken?> Ambient = new();

    private readonly ReaderProvider _readers;
    private readonly IConnectionStore _store;
    private readonly Lock _gate = new();
    private string? _remembered;
    private bool _loaded;

    public AccountContext(ReaderProvider readers, IConnectionStore store)
    {
        _readers = readers;
        _store = store;
    }

    public string? SignedIn => _readers.Reader?.AccountAddress is { Length: > 0 } address ? AccountKey.Of(address) : null;

    public string? Current => Ambient.Value is { } taken ? taken.Key : SignedIn ?? Remembered();

    public void Remember(string key)
    {
        lock (_gate)
        {
            if (_loaded && _remembered == key)
            {
                return;
            }

            _store.SetAccount(key);
            _remembered = key;
            _loaded = true;
        }
    }

    public void Forget()
    {
        lock (_gate)
        {
            _store.SetAccount(null);
            _remembered = null;
            _loaded = true;
        }
    }

    public IDisposable Use(string? key)
    {
        var before = Ambient.Value;
        Ambient.Value = new Taken(key);
        return new Restore(before);
    }

    private string? Remembered()
    {
        lock (_gate)
        {
            if (!_loaded)
            {
                _remembered = _store.Account();
                _loaded = true;
            }

            return _remembered;
        }
    }

    private sealed record Taken(string? Key);

    private sealed class Restore : IDisposable
    {
        private readonly Taken? _before;

        public Restore(Taken? before)
        {
            _before = before;
        }

        public void Dispose() => Ambient.Value = _before;
    }
}
