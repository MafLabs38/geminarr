using Geminarr.Core.Extension;

namespace Geminarr.Core.Accounts;

/// <summary>The account of the session of the extension while somebody is signed in, or the one that a piece of work has taken (<see cref="IAccountContext.Use"/>), which follows its awaits.</summary>
public sealed class AccountContext : IAccountContext
{
    private static readonly AsyncLocal<Taken?> Ambient = new();

    private readonly ExtensionInbox _inbox;

    public AccountContext(ExtensionInbox inbox)
    {
        _inbox = inbox;
    }

    public string? Current => Ambient.Value is { } taken ? taken.Key : Signed();

    public IDisposable Use(string? key)
    {
        var before = Ambient.Value;
        Ambient.Value = new Taken(key);
        return new Restore(before);
    }

    private string? Signed() => _inbox.Session is { SignedIn: true, Account: { } account } ? AccountKey.Of(account.Email) : null;

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
