using Geminarr.Core.Accounts;
using Geminarr.Core.Pipeline;

namespace Geminarr.Core.Link;

/// <summary>What the link keeps, and the account it is kept for: the destination (address, machine), the key, what waits for Mnemarr, and the accounts (SPEC §8: the stores follow the account that is signed in).</summary>
public sealed record LinkStores(IDestinationStore Destination, IKeyStore Keys, IOutboxStore Outbox, IAccountContext Accounts, IAccountWorkspaces Workspaces);
