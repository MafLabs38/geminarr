using Geminarr.Core.Accounts;

namespace Geminarr.Core.Test.Support;

/// <summary>An account context for the tests that work on stores that are not scoped: one account, always, that remembers nothing.</summary>
public sealed class FixedAccount : IAccountContext
{
    public const string Key = "0123456789abcdef";

    public string? Current => Key;

    public string? SignedIn => Key;

    public string? SignedInAddress => "account@example.org";

    public void Remember(string key)
    {
    }

    public void Forget()
    {
    }

    public IDisposable Use(string? key) => new Nothing();

    private sealed class Nothing : IDisposable
    {
        public void Dispose()
        {
        }
    }
}
