using Geminarr.Core.Configuration;
using Microsoft.Extensions.Options;

namespace Geminarr.Core.Link;

public sealed class KeyStore : IKeyStore
{
    private readonly IOptions<MnemarrOptions> _mnemarr;
    private readonly IOptions<GeminarrOptions> _geminarr;

    public KeyStore(IOptions<MnemarrOptions> mnemarr, IOptions<GeminarrOptions> geminarr)
    {
        _mnemarr = mnemarr;
        _geminarr = geminarr;
    }

    private string Path => string.IsNullOrWhiteSpace(_mnemarr.Value.KeyFile) ? System.IO.Path.Combine(_geminarr.Value.DataFolder, "mnemarr", "key") : _mnemarr.Value.KeyFile;

    public string? Read()
    {
        try
        {
            var text = File.ReadAllText(Path).Trim();
            return text.Length == 0 ? null : text;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    public void Write(string key)
    {
        var folder = System.IO.Path.GetDirectoryName(Path);
        if (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder))
        {
            CreateFolder(folder);
        }

        // Written to a file of its own first and moved over the old one: a crash never leaves half a key, and the key is never readable by another user, not even for a moment.
        var temporary = Path + ".new";
        var options = new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        using (var stream = new FileStream(temporary, options))
        {
            stream.Write(System.Text.Encoding.UTF8.GetBytes(key));
        }

        File.Move(temporary, Path, overwrite: true);
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
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A key that cannot be removed is reported by the next read; there is nothing more to do here.
        }
    }
}
