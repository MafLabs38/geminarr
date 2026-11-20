using System.Collections.Concurrent;
using Geminarr.Core.Configuration;
using Geminarr.Core.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace Geminarr.Core.Accounts;

public sealed class AccountWorkspaces : IAccountWorkspaces
{
    public const string FolderName = "accounts";

    private readonly string _dataFolder;
    private readonly ConcurrentDictionary<string, Database> _databases = new(StringComparer.Ordinal);

    public AccountWorkspaces(IOptions<GeminarrOptions> options)
    {
        _dataFolder = options.Value.DataFolder;
    }

    public string Folder(string key) => AccountKey.IsValid(key) ? Path.Combine(_dataFolder, FolderName, key) : throw new ArgumentException("That is not the key of an account.", nameof(key));

    public string Ensure(string key)
    {
        var folder = Folder(key);
        // A volume that was never mounted must not look like a fresh, empty one: the workspace is made only inside a data folder that is there.
        if (!Directory.Exists(_dataFolder))
        {
            throw new DirectoryNotFoundException("The data folder does not exist.");
        }

        MakeFolder(Path.Combine(_dataFolder, FolderName));
        MakeFolder(folder);
        return folder;
    }

    public IDatabase Database(string key) => _databases.GetOrAdd(key, workspace => new Database(Ensure(workspace)));

    public IReadOnlyList<string> Known()
    {
        var folder = Path.Combine(_dataFolder, FolderName);
        return Directory.Exists(folder) ? [.. Directory.EnumerateDirectories(folder).Select(Path.GetFileName).OfType<string>().Where(AccountKey.IsValid).Order(StringComparer.Ordinal)] : [];
    }

    public bool Exists(string key) => Directory.Exists(Folder(key));

    public void Delete(string key)
    {
        var folder = Folder(key);
        _databases.TryRemove(key, out _);
        // The pool holds the file open: it is let go of before the folder goes.
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>Only the owner reads a workspace: it holds what has not been acknowledged yet, turns included, and the key of a machine.</summary>
    private static void MakeFolder(string folder)
    {
        if (Directory.Exists(folder))
        {
            return;
        }

        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(folder);
            return;
        }

        Directory.CreateDirectory(folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }
}
