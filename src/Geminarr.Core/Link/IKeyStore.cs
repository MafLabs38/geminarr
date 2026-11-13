namespace Geminarr.Core.Link;

/// <summary>Where the key of the machine is kept: a file only the container's user can read. The key is never in the database, a log or an API answer.</summary>
public interface IKeyStore
{
    /// <summary>The key, or null when there is none (or the file is empty or cannot be read).</summary>
    string? Read();

    /// <summary>Keeps the key, and the address of the Mnemarr that gave it: the key belongs to that Mnemarr, and the binding lives next to the key, not in the database.</summary>
    void Write(string key, string address);

    /// <summary>The address the key was given for, or null for a key that the deployment gave as a file.</summary>
    string? AddressOf();

    void Delete();
}
