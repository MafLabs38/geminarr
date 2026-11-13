using Geminarr.Core.Configuration;
using Geminarr.Core.Storage;
using Geminarr.Core.Test.Access;

namespace Geminarr.Core.Test.Support;

/// <summary>A real SQLite database in a folder of its own, removed afterwards: the stores are tested against the engine they run on, not against a mock.</summary>
public sealed class TestDatabase : IDisposable
{
    public TestDatabase()
    {
        Folder = Directory.CreateTempSubdirectory("geminarr-db-").FullName;
        Settings = new GeminarrOptions { DataFolder = Folder };
        Database = new Database(Options.Of(Settings));
    }

    public string Folder { get; }

    public GeminarrOptions Settings { get; }

    public Database Database { get; }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(Folder, recursive: true);
    }
}
