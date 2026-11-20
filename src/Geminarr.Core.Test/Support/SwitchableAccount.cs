using Geminarr.Core.Accounts;

namespace Geminarr.Core.Test.Support;

/// <summary>An account context that a test signs accounts in and out of: the account that is signed in, and the one that was last.</summary>
public sealed class SwitchableAccount : IAccountContext
{
    private string? _taken;
    private bool _isTaken;

    public string? SignedInAddress { get; private set; }

    public string? SignedIn => SignedInAddress is { } address ? AccountKey.Of(address) : null;

    public string? Remembered { get; private set; }

    public string? Current => _isTaken ? _taken : SignedIn ?? Remembered;

    /// <summary>Signs an account in (null: signs out; the last account stays, as it does when a session expires).</summary>
    public void SignIn(string? address)
    {
        SignedInAddress = address;
        if (SignedIn is { } key)
        {
            Remembered = key;
        }
    }

    public void Remember(string key) => Remembered = key;

    public void Forget() => Remembered = null;

    public IDisposable Use(string? key)
    {
        var before = (_isTaken, _taken);
        (_isTaken, _taken) = (true, key);
        return new Restore(() => (_isTaken, _taken) = before);
    }

    private sealed class Restore : IDisposable
    {
        private readonly Action _restore;

        public Restore(Action restore)
        {
            _restore = restore;
        }

        public void Dispose() => _restore();
    }
}
