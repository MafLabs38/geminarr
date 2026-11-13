using Geminarr.Core.Configuration;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace Geminarr.Core.Storage;

public sealed class Database : IDatabase
{
    public const string FileName = "geminarr.db";

    private readonly string _folder;
    private readonly string _file;
    private readonly string _connectionString;
    private readonly Lock _gate = new();
    private volatile bool _ready;

    public Database(IOptions<GeminarrOptions> options)
    {
        _folder = options.Value.DataFolder;
        _file = Path.Combine(_folder, FileName);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = _file, Mode = SqliteOpenMode.ReadWriteCreate, ForeignKeys = true }.ToString();
    }

    public SqliteConnection Open()
    {
        // SQLite would create the folder itself: a volume that was never mounted would then look like a fresh, empty one.
        if (!Directory.Exists(_folder))
        {
            throw new DirectoryNotFoundException("The data folder does not exist.");
        }

        // SQLite would create the file with the umask of the process, and the WAL files after it: it is made for its owner first.
        CreateRestrictedFile();

        var connection = new SqliteConnection(_connectionString);
        try
        {
            connection.Open();
            EnsureSchema(connection);
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    private void EnsureSchema(SqliteConnection connection)
    {
        if (_ready)
        {
            return;
        }

        lock (_gate)
        {
            if (_ready)
            {
                return;
            }

            Schema.Apply(connection);
            _ready = true;
        }
    }

    /// <summary>The database holds what has not been acknowledged yet, turns included: only the owner reads it, from its creation.</summary>
    private void CreateRestrictedFile()
    {
        if (_ready || OperatingSystem.IsWindows())
        {
            return;
        }

        const UnixFileMode OwnerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        if (!File.Exists(_file))
        {
            try
            {
                using var created = new FileStream(_file, new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, UnixCreateMode = OwnerOnly });
            }
            catch (IOException) when (File.Exists(_file))
            {
                // Another connection created it first: it was made by this method too.
            }
        }

        File.SetUnixFileMode(_file, OwnerOnly);
    }
}
