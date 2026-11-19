namespace Geminarr.Core.Connection;

/// <summary>The states of the connection to the assistant (SPEC §7.2). <c>connecting</c> and <c>paused</c> come with the live view (vertical 3).</summary>
public enum ConnectionState
{
    /// <summary>No reader is configured, or no browser: nothing is read.</summary>
    NotConnected,

    /// <summary>Signed in and reading.</summary>
    Connected,

    /// <summary>Signed in, but the last pass failed: see the reason.</summary>
    Degraded,

    /// <summary>The session is gone (several passes in a row found nobody signed in): a person must sign in again.</summary>
    NeedsLogin,

    /// <summary>The operator asked for quiet, until a moment or until they resume.</summary>
    Paused,
}

/// <summary>The reasons a connection is not simply connected: the same codes as the health, the signal to Mnemarr and the page.</summary>
public static class ConnectionReasons
{
    public const string NotConnected = "not-connected";
    public const string NeedsLogin = "needs-login";
    public const string ReaderBroken = "reader-broken";
    public const string RateLimited = "rate-limited";
    public const string Unreachable = "unreachable";
    public const string Paused = "paused";

    /// <summary>The operator disconnected: the account is being signed out of the browser by the extension, and nothing is read until somebody signs in again. It is kept in the record, so that a restart does not forget a sign-out that was not done yet.</summary>
    public const string Disconnected = "disconnected";

    /// <summary>The browser container could not be reached (not started, its desktop address wrong, a limit): a matter for the deployment, not a page that changed, so nobody is asked to sign in or to update.</summary>
    public const string BrowserFailed = "browser-failed";

    /// <summary>A port of the namespace that Geminarr shares with the browser listens off the loopback: the web desktop could be reached by something else than Geminarr (ADR 0017), so the desktop stays closed.</summary>
    public const string BrowserExposed = "browser-exposed";

    /// <summary>Geminarr's own process is not limited by the egress guard (ADR 0020).</summary>
    public const string EgressUnguarded = "egress-unguarded";

    /// <summary>The extension of the browser has not said anything for a while (ADR 0022): the browser is not running it, or it cannot reach Geminarr.</summary>
    public const string ExtensionSilent = "extension-silent";
}

/// <summary>The row of the connection: its state, since when, the last pass that worked, the failures in a row, and why.</summary>
public sealed record ConnectionRecord(ConnectionState State, DateTimeOffset Since, DateTimeOffset? LastOkAt, int ConsecutiveFailures, string? LastReason, DateTimeOffset? PausedUntil = null, ConnectionBefore? Before = null, bool SignOutPending = false)
{
    /// <summary>Whether the operator disconnected and nobody has connected since: the reason says so, and a pause keeps it in the state it took over.</summary>
    public bool IsDisconnected => LastReason == ConnectionReasons.Disconnected || Before?.Reason == ConnectionReasons.Disconnected;
}

/// <summary>What the connection was before a pause took it over, so that it goes back to it: trying to sign in, or asking for quiet, does not change what is known of the session.</summary>
public sealed record ConnectionBefore(ConnectionState State, string? Reason, int Failures);
