namespace Geminarr.Core.Pipeline;

public static class RunKinds
{
    public const string Pass = "pass";

    /// <summary>A pass between two readings that only sent what waited and told Mnemarr the machine is alive; written when its state changed or it sent turns.</summary>
    public const string Heartbeat = "heartbeat";
}

public static class RunOutcomes
{
    public const string Ok = "ok";
    public const string Partial = "partial";
    public const string Failed = "failed";
    public const string Skipped = "skipped";
}

/// <summary>How a pass ended: the outcome and why, the counts, and how long it took.</summary>
public sealed record RunOutcome(string Outcome, string? Reason, int Listed, int Opened, int TurnsSent, long DurationMs);

/// <summary>One pass: when, how it ended and why, and what it did. Counts only: nothing a conversation said.</summary>
public sealed record RunRecord(long Id, string Kind, DateTimeOffset StartedAt, DateTimeOffset? EndedAt, string? Outcome, string? Reason, int Listed, int Opened, int TurnsSent, long DurationMs);
