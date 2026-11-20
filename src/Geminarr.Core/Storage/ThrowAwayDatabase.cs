using Microsoft.Data.Sqlite;

namespace Geminarr.Core.Storage;

/// <summary>
/// What the stores of an account answer from when no account is signed in (SPEC §8.2): an empty database with the schema, in memory, that each call makes again. A read finds nothing, and what is written is gone with
/// the connection: nothing of an account is ever kept for nobody.
/// </summary>
public sealed class ThrowAwayDatabase : IDatabase
{
    public SqliteConnection Open()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        try
        {
            connection.Open();
            Schema.Apply(connection);
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }
}
