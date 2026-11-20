using Geminarr.Core.Storage;
using Microsoft.Data.Sqlite;

namespace Geminarr.Core.Accounts;

/// <summary>
/// The database of what belongs to an account (SPEC §8): the one of the workspace of the account that the work is for (<see cref="IAccountContext"/>), or, with no account, an empty one that is thrown away. The stores
/// ask it at every call, so that a switch of account is followed without any of them being made again.
/// </summary>
public sealed class AccountDatabase : IDatabase
{
    private readonly IAccountContext _account;
    private readonly IAccountWorkspaces _workspaces;
    private readonly ThrowAwayDatabase _none = new();

    public AccountDatabase(IAccountContext account, IAccountWorkspaces workspaces)
    {
        _account = account;
        _workspaces = workspaces;
    }

    public SqliteConnection Open() => _account.Current is { } key ? _workspaces.Database(key).Open() : _none.Open();
}
