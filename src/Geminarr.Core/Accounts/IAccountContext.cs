namespace Geminarr.Core.Accounts;

/// <summary>Which account the work is for (SPEC §8): the one that is signed in, then the one that was signed in last, unless a piece of work has taken one for its whole length (<see cref="Use"/>).</summary>
public interface IAccountContext
{
    /// <summary>The key of the account to work on, or null when there is none: nobody is signed in and the operator disconnected (or nobody ever signed in).</summary>
    string? Current { get; }

    /// <summary>The key of the account that is signed in now, or null.</summary>
    string? SignedIn { get; }

    /// <summary>The address of the account that is signed in now, or null: in memory for the page and for the name of a machine, never written to disk as it is.</summary>
    string? SignedInAddress { get; }

    /// <summary>
    /// Keeps this account as the one that was signed in last: when its session ends without the operator asking (it expired), the machine of this account is still the one that tells Mnemarr, which sends the mail that
    /// asks for a sign-in. It is kept in the root database (the key, never the address) and survives a restart.
    /// </summary>
    void Remember(string key);

    /// <summary>The operator disconnected: no account is the last one any more, and the machine of the account that was is silent (SPEC §8.2, rule 7).</summary>
    void Forget();

    /// <summary>
    /// Works on this account (null: on none) until the result is disposed, whatever happens to the session in the meantime: what is started in between, and what is awaited, sees the same account. A pass takes the account
    /// that it began with, so that a sign-out or a switch in the middle of it never writes to another account's workspace.
    /// </summary>
    IDisposable Use(string? key);

    /// <summary>Whether a piece of work has taken this account and is not done: its workspace is not forgotten under it (the work would make it again).</summary>
    bool InUse(string key);
}
