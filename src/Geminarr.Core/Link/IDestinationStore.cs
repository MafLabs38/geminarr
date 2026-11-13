namespace Geminarr.Core.Link;

public interface IDestinationStore
{
    DestinationRecord? Get();

    /// <summary>Keeps an address when there is no row yet: a row that exists, with the address a key was given for, is never changed by this.</summary>
    void EnsureAddress(string url);

    /// <summary>Keeps the address (and forgets the machine and the link: it is another Mnemarr).</summary>
    void SaveAddress(string url);

    /// <summary>The key was obtained: the machine's name in Mnemarr and the moment.</summary>
    void SaveLinked(string machine, DateTimeOffset at);

    /// <summary>The result of the last heartbeat: when, and the code of the problem (null when it worked).</summary>
    void RecordHeartbeat(DateTimeOffset at, string? problem);

    void Clear();
}
