namespace Geminarr.Core.Accounts;

/// <summary>Which account the work is for (SPEC §8): the one that is signed in, unless a piece of work has taken one for its whole length (<see cref="Use"/>).</summary>
public interface IAccountContext
{
    /// <summary>The key of the account to work on, or null when there is none (nobody is signed in).</summary>
    string? Current { get; }

    /// <summary>
    /// Works on this account (null: on none) until the result is disposed, whatever happens to the session in the meantime: what is started in between, and what is awaited, sees the same account. A pass takes the account
    /// that was signed in when it began, so that a sign-out or a switch in the middle of it never writes to another account's workspace.
    /// </summary>
    IDisposable Use(string? key);
}
