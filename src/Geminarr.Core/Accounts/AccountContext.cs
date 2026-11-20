using System.Collections.Concurrent;
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
    private readonly ConcurrentDictionary<string, int> _inUse = new();
    private readonly Lock _gate = new();
    private string? _remembered;
    private bool _loaded;

    public AccountContext(ReaderProvider readers, IConnectionStore store)
    {
        _readers = readers;
        _store = store;
    }

    public string? SignedInAddress => _readers.Reader?.AccountAddress is { Length: > 0 } address ? address : null;

    public string? SignedIn => SignedInAddress is { } address ? AccountKey.Of(address) : null;

    /// <summary>One reading of the reader, so that the answer is of one state of the session: the address, and whether a session is signed in to somebody that cannot be named.</summary>
    private (string? Address, bool WithoutAccount) Session()
    {
        var reader = _readers.Reader;
        return (reader?.AccountAddress is { Length: > 0 } address ? address : null, reader?.SignedInWithoutAccount == true);
    }

    /// <summary>
    /// A session that is signed in to an account that cannot be read (the label of the page did not parse) is not the account that was signed in last: nothing is read for it, and nothing is written to the other's workspace.
    /// The last account is the one to work on only while nobody is signed in.
    /// </summary>
    public string? Current
    {
        get
        {
            if (Ambient.Value is { } taken)
            {
                return taken.Key;
            }

            var (address, withoutAccount) = Session();
            if (address is not null)
            {
                return AccountKey.Of(address);
            }

            return withoutAccount ? null : Remembered();
        }
    }

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
        if (key is not null)
        {
            _inUse.AddOrUpdate(key, 1, (_, count) => count + 1);
        }

        return new Restore(this, before, key);
    }

    public bool InUse(string key) => _inUse.TryGetValue(key, out var count) && count > 0;

    private void Release(string key) => _inUse.AddOrUpdate(key, 0, (_, count) => Math.Max(count - 1, 0));

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
        private readonly AccountContext _owner;
        private readonly Taken? _before;
        private readonly string? _key;
        private int _disposed;

        public Restore(AccountContext owner, Taken? before, string? key)
        {
            _owner = owner;
            _before = before;
            _key = key;
        }

        public void Dispose()
        {
            // Once: a second dispose neither puts back an account that was left since nor frees what another piece of work holds.
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            Ambient.Value = _before;
            if (_key is not null)
            {
                _owner.Release(_key);
            }
        }
    }
}
