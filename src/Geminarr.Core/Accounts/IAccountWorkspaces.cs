using Geminarr.Core.Storage;

namespace Geminarr.Core.Accounts;

/// <summary>The workspaces of the accounts (SPEC §8.1): a folder of the data folder for each, with its database and its Mnemarr key.</summary>
public interface IAccountWorkspaces
{
    /// <summary>The database of an account, whose workspace is made at the first use. The key must be a key (<see cref="AccountKey.IsValid"/>): nothing else can name a folder.</summary>
    IDatabase Database(string key);

    /// <summary>The folder of the workspace of an account (it may not exist yet).</summary>
    string Folder(string key);

    /// <summary>The keys of the accounts that have a workspace, in order.</summary>
    IReadOnlyList<string> Known();

    bool Exists(string key);

    /// <summary>Deletes the workspace of an account: its database and its key. What Mnemarr holds is not touched.</summary>
    void Delete(string key);
}
