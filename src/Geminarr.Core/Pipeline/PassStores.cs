using Geminarr.Core.Accounts;

namespace Geminarr.Core.Pipeline;

/// <summary>What a pass keeps: the conversations it knows, what waits for Mnemarr, its own runs, and the account that they belong to (SPEC §8: the stores follow it); the workspaces, to send what waits for an account that is signed out.</summary>
public sealed record PassStores(IConversationStore Conversations, IOutboxStore Outbox, IRunStore Runs, IAccountContext Accounts, IAccountWorkspaces? Workspaces = null);
