namespace Geminarr.Core.Pipeline;

/// <summary>What a pass did: how it ended and why, and the counts. Never what a conversation said.</summary>
public sealed record PassResult(string Outcome, string? Reason, int Listed, int Opened, int TurnsSent);
