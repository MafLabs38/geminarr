namespace Geminarr.Core.Link;

/// <summary>Where the key of the machine is kept: a file only the container's user can read. The key is never in the database, a log or an API answer.</summary>
public interface IKeyStore
{
    /// <summary>The key, or null when there is none (or the file is empty or cannot be read).</summary>
    string? Read();

    void Write(string key);

    void Delete();
}
