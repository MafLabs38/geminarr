using Geminarr.Core.Link;
using Geminarr.Core.Pipeline;

namespace Geminarr.Core.Accounts;

/// <summary>One account as the pages show it (SPEC §8.3): an opaque id (never the address), its machine, and what it holds. Nothing of a conversation.</summary>
public sealed record AccountSummary(string Id, string? Machine, bool Active, bool SignedIn, bool Linked, DateTimeOffset? LastSignOfLifeAt, long WaitingLines, int Conversations, DateTimeOffset? LastReadAt);

/// <summary>The accounts that are known: the workspaces that exist, and the account that is signed in even before its workspace is made. Reads each workspace on its own, whichever account is the active one.</summary>
public sealed class AccountService
{
    /// <summary>The length of the id that the pages and the API use for an account: the start of its key.</summary>
    public const int IdLength = 8;

    private readonly IAccountContext _accounts;
    private readonly IAccountWorkspaces _workspaces;
    private readonly LinkService _link;

    public AccountService(IAccountContext accounts, IAccountWorkspaces workspaces, LinkService link)
    {
        _accounts = accounts;
        _workspaces = workspaces;
        _link = link;
    }

    public static string IdOf(string key) => key[..IdLength];

    public IReadOnlyList<AccountSummary> List()
    {
        var known = _workspaces.Known();
        var current = _accounts.Current;
        return [.. Keys().Select(key => Summary(key, known.Contains(key), current))];
    }

    /// <summary>The key of the account that an id names, among the accounts that are known; null when none does.</summary>
    public string? KeyOf(string id)
    {
        var matches = Keys().Where(key => string.Equals(IdOf(key), id, StringComparison.Ordinal)).ToList();
        return matches.Count == 1 ? matches[0] : null;
    }

    /// <summary>The conversations of an account that is not the one that the stores follow: read from its workspace, as they are.</summary>
    public IConversationStore ConversationsOf(string key) => new ConversationStore(_workspaces.Database(key));

    /// <summary>Forgets an account: its workspace is deleted. The account that the stores follow is not forgotten (it is being read, or its session expired): <see cref="ForgetResult.Active"/>.</summary>
    public ForgetResult Forget(string id)
    {
        if (KeyOf(id) is not { } key)
        {
            return ForgetResult.Unknown;
        }

        if (key == _accounts.Current)
        {
            return ForgetResult.Active;
        }

        if (!_workspaces.Exists(key))
        {
            return ForgetResult.Unknown;
        }

        _workspaces.Delete(key);
        return ForgetResult.Forgotten;
    }

    /// <summary>The keys of the accounts that are known, in order: the workspaces, then the account that the stores follow if it has none yet.</summary>
    private IReadOnlyList<string> Keys()
    {
        var known = _workspaces.Known();
        return _accounts.Current is { } current && !known.Contains(current) ? [.. known, current] : known;
    }

    private AccountSummary Summary(string key, bool exists, string? current)
    {
        var active = key == current;
        var signedIn = key == _accounts.SignedIn;
        if (!exists)
        {
            // An account that is signed in and has no workspace yet: nothing is kept for it, and its machine is the one that it will be paired as.
            return new AccountSummary(IdOf(key), active ? _link.DerivedMachine() : null, active, signedIn, Linked: false, null, 0, 0, null);
        }

        var database = _workspaces.Database(key);
        var destination = new DestinationStore(database).Get();
        var conversations = new ConversationStore(database);
        return new AccountSummary(
            IdOf(key),
            active ? _link.Status().Machine : destination?.Machine,
            active,
            signedIn,
            active ? _link.Status().Linked : File.Exists(Path.Combine(_workspaces.Folder(key), "mnemarr", "key")),
            destination?.LastHeartbeatAt,
            new OutboxStore(database).PendingLines(),
            conversations.Count(),
            conversations.Recent(1).FirstOrDefault()?.LastOpenedAt);
    }
}

public enum ForgetResult
{
    Forgotten,
    Unknown,
    Active,
}
