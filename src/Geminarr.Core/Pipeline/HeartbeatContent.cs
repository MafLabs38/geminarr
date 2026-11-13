using Geminarr.Core.Connection;
using Geminarr.Core.Link;

namespace Geminarr.Core.Pipeline;

/// <summary>What a heartbeat says to Mnemarr, from the state of the connection and of the destination: short sentences with no path and no content, and the two codes that ask a person for something.</summary>
public static class HeartbeatContent
{
    public static string ProblemOf(SinkFailure failure) => failure switch
    {
        SinkFailure.NotLinked => DestinationProblems.NotLinked,
        SinkFailure.InsecureAddress => DestinationProblems.InsecureAddress,
        SinkFailure.Refused => DestinationProblems.Refused,
        SinkFailure.Rejected => DestinationProblems.Rejected,
        _ => DestinationProblems.Unreachable,
    };

    /// <summary>A short sentence with no path and no content: what Mnemarr shows next to the machine.</summary>
    public static string? ErrorOf(ConnectionRecord connection) => connection.State switch
    {
        ConnectionState.Connected => null,
        ConnectionState.NotConnected => "Not connected.",
        ConnectionState.NeedsLogin => "The session has expired.",
        _ => connection.LastReason switch
        {
            ConnectionReasons.ReaderBroken => "The assistant's page is no longer what the reader knows.",
            ConnectionReasons.RateLimited => "The assistant asked to slow down.",
            ConnectionReasons.Unreachable => "The assistant could not be reached.",
            _ => "The last pass failed.",
        },
    };

    /// <summary>What a person is asked for: only the two codes Mnemarr knows. The address is where to answer, given only when Mnemarr would keep it (https, or loopback).</summary>
    public static Attention? AttentionOf(ConnectionRecord connection, string? publicUrl)
    {
        string? code = null;
        if (connection.State == ConnectionState.NeedsLogin)
        {
            code = ConnectionReasons.NeedsLogin;
        }
        else if (connection.LastReason == ConnectionReasons.ReaderBroken)
        {
            code = ConnectionReasons.ReaderBroken;
        }

        if (code is null)
        {
            return null;
        }

        return new Attention(code, MnemarrAddress.AttentionLink(publicUrl));
    }
}
