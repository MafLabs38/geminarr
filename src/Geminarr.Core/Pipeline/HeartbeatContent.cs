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
    public static string? ErrorOf(ConnectionRecord connection) => Effective(connection).State switch
    {
        ConnectionState.Connected => null,
        ConnectionState.Paused => null,
        ConnectionState.Connecting => null,
        ConnectionState.NotConnected => "Not connected.",
        ConnectionState.NeedsLogin => "The session has expired.",
        _ => Effective(connection).LastReason switch
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
        var effective = Effective(connection);
        string? code = null;
        if (effective.State == ConnectionState.NeedsLogin)
        {
            code = ConnectionReasons.NeedsLogin;
        }
        else if (effective.LastReason == ConnectionReasons.ReaderBroken)
        {
            code = ConnectionReasons.ReaderBroken;
        }

        if (code is null)
        {
            return null;
        }

        return new Attention(code, MnemarrAddress.AttentionLink(publicUrl));
    }

    /// <summary>
    /// What the connection is for Mnemarr: a person signing in, or a pause, does not change what is wrong (a heartbeat without the attention would end the episode that asked for the sign-in, and a mail could follow
    /// when it comes back), so the state it took over is the one that is told.
    /// </summary>
    private static ConnectionRecord Effective(ConnectionRecord connection) =>
        connection is { State: ConnectionState.Connecting or ConnectionState.Paused, Before: { } before }
            ? connection with { State = before.State, LastReason = before.Reason, ConsecutiveFailures = before.Failures }
            : connection;
}
