using Geminarr.Core.Accounts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Geminarr.Api.V1.Accounts;

/// <summary>
/// One account as the operator sees it (SPEC §8.3). <b>The id is opaque and the address is never in it</b>: the machine, named from the address, is what says which account it is. Nothing of a conversation.
/// </summary>
public sealed record AccountResource(string Id, string? Machine, bool Active, bool SignedIn, bool Linked, DateTimeOffset? LastSignOfLifeAt, long WaitingLines, int Conversations, DateTimeOffset? LastReadAt)
{
    public static AccountResource From(AccountSummary account) =>
        new(account.Id, account.Machine, account.Active, account.SignedIn, account.Linked, account.LastSignOfLifeAt, account.WaitingLines, account.Conversations, account.LastReadAt);
}

public sealed record AccountsResource(IReadOnlyList<AccountResource> Accounts);

/// <summary>The accounts that are known, and the way to forget one (F-ACT-03).</summary>
[ApiController]
[Route("api/v1/accounts")]
public sealed class AccountsController : ControllerBase
{
    private readonly AccountService _accounts;

    public AccountsController(AccountService accounts)
    {
        _accounts = accounts;
    }

    /// <summary>The accounts that have a workspace, and the one that is signed in (even before its workspace is made).</summary>
    [HttpGet]
    [ProducesResponseType<AccountsResource>(StatusCodes.Status200OK)]
    public AccountsResource Get() => new([.. _accounts.List().Select(AccountResource.From)]);

    /// <summary>Forgets an account: its workspace (its conversations, its outbox, its runs, its key) is deleted. What Mnemarr holds is not touched. The account that is being read is not forgotten: disconnect first.</summary>
    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public IActionResult Forget(string id) => _accounts.Forget(id) switch
    {
        ForgetResult.Forgotten => NoContent(),
        ForgetResult.Active => Problem(statusCode: StatusCodes.Status409Conflict, title: "That account is the one that is being read (or whose session expired): disconnect first."),
        _ => Problem(statusCode: StatusCodes.Status404NotFound, title: "There is no such account."),
    };
}
