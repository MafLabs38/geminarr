namespace Geminarr.Core.Connection;

/// <summary>The states of the connection to the assistant (SPEC §7.2). <c>connecting</c> and <c>paused</c> come with the live view (vertical 3).</summary>
public enum ConnectionState
{
    /// <summary>No reader is configured, or no profile: nothing is read.</summary>
    NotConnected,

    /// <summary>Signed in and reading.</summary>
    Connected,

    /// <summary>Signed in, but the last pass failed: see the reason.</summary>
    Degraded,

    /// <summary>The session is gone (several passes in a row found nobody signed in): a person must sign in again.</summary>
    NeedsLogin,
}

/// <summary>The reasons a connection is not simply connected: the same codes as the health, the signal to Mnemarr and the page.</summary>
public static class ConnectionReasons
{
    public const string NotConnected = "not-connected";
    public const string NeedsLogin = "needs-login";
    public const string ReaderBroken = "reader-broken";
    public const string RateLimited = "rate-limited";
    public const string Unreachable = "unreachable";
}

/// <summary>The row of the connection: its state, since when, the last pass that worked, the failures in a row, and why.</summary>
public sealed record ConnectionRecord(ConnectionState State, DateTimeOffset Since, DateTimeOffset? LastOkAt, int ConsecutiveFailures, string? LastReason);
