namespace Geminarr.Core.Connection;

public interface IConnectionStore
{
    /// <summary>The state of the connection; not connected, since now, when nothing was ever recorded.</summary>
    ConnectionRecord Get();

    void Save(ConnectionRecord record);
}
