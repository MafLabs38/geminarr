using Geminarr.Core.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Geminarr.Core.Link;

public sealed class KeyStore : IKeyStore
{
    private readonly IOptions<MnemarrOptions> _mnemarr;
    private readonly IOptions<GeminarrOptions> _geminarr;
    private readonly ILogger<KeyStore> _logger;

    public KeyStore(IOptions<MnemarrOptions> mnemarr, IOptions<GeminarrOptions> geminarr, ILogger<KeyStore>? logger = null)
    {
        _mnemarr = mnemarr;
        _geminarr = geminarr;
        _logger = logger ?? NullLogger<KeyStore>.Instance;
    }

    private string Path => string.IsNullOrWhiteSpace(_mnemarr.Value.KeyFile) ? System.IO.Path.Combine(_geminarr.Value.DataFolder, "mnemarr", "key") : _mnemarr.Value.KeyFile;

    public string? Read()
    {
        try
        {
            var text = File.ReadAllText(Path).Trim();
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
        try
        {
            var text = File.ReadAllText(Path + ".address").Trim();
            return text.Length == 0 ? null : text;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    public void Write(string key, string address)
    {
        var folder = System.IO.Path.GetDirectoryName(Path);
        if (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder))
        {
            CreateFolder(folder);
        }

        // Written to a file of its own first and moved over the old one: a crash never leaves half a key, and the key is never readable by another user, not even for a moment.
        // The address that binds it is written the same way (it decides which Mnemarr may be given the key), and after it: a crash between the two leaves a key without its address, which is bound to nothing, never an old key bound to a new address.
        WriteOwnerOnly(Path, key);
        WriteOwnerOnly(Path + ".address", address);
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
        try
        {
            File.Delete(Path);
            File.Delete(Path + ".address");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A key that cannot be removed is reported by the next read; there is nothing more to do here.
        }
    }
}
