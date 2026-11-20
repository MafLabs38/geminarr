using System.Collections.Concurrent;
using Geminarr.Core.Accounts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Geminarr.Core.Link;

public sealed class KeyStore : IKeyStore
{
    private readonly IOptions<MnemarrOptions> _mnemarr;
    private readonly IAccountContext _accounts;
    private readonly IAccountWorkspaces _workspaces;
    private readonly ILogger<KeyStore> _logger;
    private readonly ConcurrentDictionary<string, bool> _refused = new();

    public KeyStore(IOptions<MnemarrOptions> mnemarr, IAccountContext accounts, IAccountWorkspaces workspaces, ILogger<KeyStore>? logger = null)
    {
        _mnemarr = mnemarr;
        _accounts = accounts;
        _workspaces = workspaces;
        _logger = logger ?? NullLogger<KeyStore>.Instance;
    }

    /// <summary>
    /// The file of the key for an account: the one that the deployment gives (one key is one machine, so it serves the first account only, SPEC §8.2), or the one of the workspace of the account. Null when there is none:
    /// no account, no key; or an account that the key of the deployment does not serve.
    /// </summary>
    private string? PathFor(string? account)
    {
        if (!string.IsNullOrWhiteSpace(_mnemarr.Value.KeyFile))
        {
            return KeyFileServes(account) ? _mnemarr.Value.KeyFile : null;
        }

        return account is null ? null : System.IO.Path.Combine(_workspaces.Folder(account), "mnemarr", "key");
    }

    /// <summary>A key from the deployment is one machine: it serves nobody while no account is signed in (the page says it is there), and the one account that exists, never a second one (its notes would go to the machine of the first).</summary>
    private bool KeyFileServes(string? account)
    {
        if (account is null || !_workspaces.Known().Any(other => other != account))
        {
            return true;
        }

        if (_refused.TryAdd(account, true))
        {
            _logger.LogWarning("A key given by the deployment serves one account: a second account is not linked to it. Pair it instead (unset GEMINARR__MNEMARR__KEYFILE).");
        }

        return false;
    }

    public string? Read() => ReadFile(PathFor(_accounts.Current));

    public bool ExistsFor(string account) => ReadFile(PathFor(account)) is not null;

    private string? ReadFile(string? path)
    {
        if (path is null)
        {
            return null;
        }

        try
        {
            var text = File.ReadAllText(path).Trim();
            return text.Length == 0 ? null : text;
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            // A key that is there and cannot be read is not a key that is missing: the page says "not linked", and this says why.
            _logger.LogWarning(ex, "The file of the Mnemarr key exists and cannot be read: check its permissions.");
            return null;
        }
    }

    public string? AddressOf()
    {
        if (PathFor(_accounts.Current) is not { } path)
        {
            return null;
        }

        try
        {
            var text = File.ReadAllText(path + ".address").Trim();
            return text.Length == 0 ? null : text;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    public void Write(string key, string address)
    {
        // The account is read once: the folder that is made and the file that is written are those of the same account, whatever happens to the session in between.
        var account = _accounts.Current;
        var path = PathFor(account) ?? throw new InvalidOperationException("No account is signed in, or the key of the deployment does not serve it: its key has nowhere to be kept.");
        if (string.IsNullOrWhiteSpace(_mnemarr.Value.KeyFile) && account is not null)
        {
            // The workspace is made first, for its owner alone: the folder of the key is made inside it.
            _workspaces.Ensure(account);
        }

        var folder = System.IO.Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder))
        {
            CreateFolder(folder);
        }

        // Written to a file of its own first and moved over the old one: a crash never leaves half a key, and the key is never readable by another user, not even for a moment.
        // The address that binds it is written the same way (it decides which Mnemarr may be given the key), and after it: a crash between the two leaves a key without its address, which is not bound to an address (as a key that the deployment gives), never an old key bound to a new address.
        WriteOwnerOnly(path, key);
        WriteOwnerOnly(path + ".address", address);
    }

    private static void WriteOwnerOnly(string path, string text)
    {
        var temporary = path + ".new";
        // A file that is already there keeps its mode, and a link would be followed: it is removed first (a link itself, not what it points to).
        File.Delete(temporary);
        var options = new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        using (var stream = new FileStream(temporary, options))
        {
            stream.Write(System.Text.Encoding.UTF8.GetBytes(text));
        }

        File.Move(temporary, path, overwrite: true);
    }

    private static void CreateFolder(string folder)
    {
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(folder);
            return;
        }

        Directory.CreateDirectory(folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    public void Delete()
    {
        if (PathFor(_accounts.Current) is not { } path)
        {
            return;
        }

        try
        {
            File.Delete(path);
            File.Delete(path + ".address");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A key that cannot be removed is reported by the next read; there is nothing more to do here.
        }
    }
}
