namespace Geminarr.Core.Pipeline;

/// <summary>What is known of a conversation. <b>Never the text of a turn</b>: only counts, a hash of the last turn sent, and states.</summary>
public sealed record ConversationRecord(
    string Id,
    string? Title,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt,
    DateTimeOffset? LastOpenedAt,
    int? ListRank,
    int TurnsKnown,
    int TurnsSent,
    string? LastTurnHash,
    int RewrittenTurns,
    string Source,
    string State,
    string? LastError);

public static class ConversationStates
{
    public const string New = "new";
    public const string Synced = "synced";
    public const string Gone = "gone";
    public const string Error = "error";
}

public static class ConversationSources
{
    public const string Web = "web";
    public const string Takeout = "takeout";
}
