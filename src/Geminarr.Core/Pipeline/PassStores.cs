namespace Geminarr.Core.Pipeline;

/// <summary>What a pass keeps: the conversations it knows, what waits for Mnemarr, and its own runs.</summary>
public sealed record PassStores(IConversationStore Conversations, IOutboxStore Outbox, IRunStore Runs);
