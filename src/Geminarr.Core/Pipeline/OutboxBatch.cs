namespace Geminarr.Core.Pipeline;

/// <summary>A batch of lines of one conversation that Mnemarr has not acknowledged: its place in the conversation, its lines, and how it went so far.</summary>
public sealed record OutboxBatch(long Id, string ConversationId, long FromPosition, IReadOnlyList<string> Lines, DateTimeOffset CreatedAt, int Attempts, string? LastError);
