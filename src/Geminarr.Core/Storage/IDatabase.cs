using Microsoft.Data.Sqlite;

namespace Geminarr.Core.Storage;

/// <summary>Geminarr's SQLite database (ADR 0014): what each vertical keeps in it is a step of its <see cref="Schema"/>.</summary>
public interface IDatabase
{
    /// <summary>An open connection, with the schema in place. The caller disposes it.</summary>
    SqliteConnection Open();
}
