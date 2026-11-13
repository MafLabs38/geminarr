namespace Geminarr.Core.Pipeline;

/// <summary>Where the lines go: Mnemarr (SPEC §5). Every call can fail with a <see cref="SinkException"/> that says why.</summary>
public interface IConversationSink
{
    Task<SinkPositions> PositionsAsync(CancellationToken cancellationToken);

    Task<SinkResult> SendAsync(string session, long from, IReadOnlyList<string> lines, CancellationToken cancellationToken);

    Task HeartbeatAsync(Heartbeat heartbeat, CancellationToken cancellationToken);
}

/// <summary>The signal at the end of a pass: the version, the lines still to send, a short sentence when something is wrong, and what a person is asked to do.</summary>
public sealed record Heartbeat(string Version, long PendingLines, string? Error, Attention? Attention);

/// <summary>What the operator is asked for: a code from the closed list of Mnemarr (<c>needs-login</c>, <c>reader-broken</c>) and where they can answer.</summary>
public sealed record Attention(string Code, string? Url);

/// <summary>The kinds of failure of the destination, which the health and the page name by code.</summary>
public enum SinkFailure
{
    /// <summary>No destination is set, or no key: nothing to send to.</summary>
    NotLinked,

    /// <summary>The address is not safe to talk to (plain http to a host that is not the loopback), and the sandbox switch is off.</summary>
    InsecureAddress,

    /// <summary>Mnemarr answered 401 or 403: the key was revoked or the machine removed.</summary>
    Refused,

    /// <summary>Mnemarr does not answer, or not with an answer: wait, the outbox keeps the turns.</summary>
    Unreachable,

    /// <summary>Mnemarr said no to what was sent (400, 413, 422): a bug or a limit, never retried as it is.</summary>
    Rejected,
}

public sealed class SinkException : Exception
{
    public SinkException(SinkFailure failure, string message)
        : base(message)
    {
        Failure = failure;
    }

    public SinkFailure Failure { get; }
}
