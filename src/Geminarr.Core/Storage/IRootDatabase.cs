namespace Geminarr.Core.Storage;

/// <summary>The database of the data folder itself (SPEC §4, §8): what is not an account's, such as the state of the connection. What belongs to an account is in its workspace and comes from <see cref="IDatabase"/>.</summary>
public interface IRootDatabase : IDatabase
{
}
