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

    public KeyStore(IOptions<MnemarrOptions> mnemarr, IAccountContext accounts, IAccountWorkspaces workspaces, ILogger<KeyStore>? logger = null)
    {
        _mnemarr = mnemarr;
        _accounts = accounts;
        _workspaces = workspaces;
        _logger = logger ?? NullLogger<KeyStore>.Instance;
    }

    /// <summary>
    /// The file of the key: the one that the deployment gives (one key for one machine, SPEC §8.2), or the one of the workspace of the account that the work is for. Null when there is none: no account, no key.
    /// </summary>
    private string? Path
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(_mnemarr.Value.KeyFile))
            {
                return _mnemarr.Value.KeyFile;
            }

            return _accounts.Current is { } key ? System.IO.Path.Combine(_workspaces.Folder(key), "mnemarr", "key") : null;
        }
    }

    public string? Read()
    {
        if (Path is not { } path)
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
        if (Path is not { } path)
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
        var path = Path ?? throw new InvalidOperationException("No account is signed in: its key has nowhere to be kept.");
        if (string.IsNullOrWhiteSpace(_mnemarr.Value.KeyFile) && _accounts.Current is { } account)
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
        if (Path is not { } path)
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
