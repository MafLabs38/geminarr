using Geminarr.Core.Accounts;

namespace Geminarr.Core.Test.Support;

/// <summary>An account context that says what was asked of it: for the tests of what the connection does about the account.</summary>
public sealed class RecordingAccount : IAccountContext
{
    public int Forgotten { get; private set; }

    public string? Current => FixedAccount.Key;

    public string? SignedIn => FixedAccount.Key;

    public string? SignedInAddress => "account@example.org";

    public void Remember(string key)
    {
    }

    public void Forget() => Forgotten++;

    public IDisposable Use(string? key) => new Nothing();

    private sealed class Nothing : IDisposable
    {
        public void Dispose()
        {
        }
    }
}
