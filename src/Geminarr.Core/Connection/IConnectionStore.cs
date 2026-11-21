namespace Geminarr.Core.Connection;

public interface IConnectionStore
{
    /// <summary>The state of the connection; not connected, since now, when nothing was ever recorded.</summary>
    ConnectionRecord Get();

    void Save(ConnectionRecord record);

    /// <summary>The key of the account that was signed in last, kept until the operator disconnects (SPEC §8.2): an expired session is told to Mnemarr by the machine of that account. Null when there is none.</summary>
    string? Account();

    void SetAccount(string? key);
}
