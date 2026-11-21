using Geminarr.Core.Accounts;
using Geminarr.Core.Extension;
using Geminarr.Core.Pipeline;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Geminarr.Api.V1.Conversations;

/// <summary>
/// One conversation as the page shows it: the counts and the state, and when it was first seen and last opened. <b>Never a title and never a turn</b>: Geminarr keeps neither (D-06), a conversation is read in Mnemarr.
/// </summary>
public sealed record ConversationResource(string Id, string Source, string State, int TurnsKnown, int TurnsSent, int RewrittenTurns, int? ListRank, DateTimeOffset FirstSeenAt, DateTimeOffset LastSeenAt, DateTimeOffset? LastOpenedAt, string? LastError)
{
    public static ConversationResource From(ConversationRecord record) =>
        new(record.Id, record.Source, record.State, record.TurnsKnown, record.TurnsSent, record.RewrittenTurns, record.ListRank, record.FirstSeenAt, record.LastSeenAt, record.LastOpenedAt, record.LastError);
}

public sealed record BackfillResource(bool Enabled, int PerPass, int Listed, int Known, bool ListComplete, bool Done)
{
    public static BackfillResource From(BackfillProgress progress) => new(progress.Enabled, progress.PerPass, progress.Listed, progress.Known, progress.ListComplete, progress.Done);
}

/// <summary>The conversations of an account, and the progress of its history: only for the account that is being read (the list that the page showed is that account's), null for another one.</summary>
public sealed record ConversationsResource(int Total, IReadOnlyList<ConversationResource> Conversations, BackfillResource? Backfill);

/// <summary>What Geminarr knows of the conversations (F-VIEW-02), metadata only, and how far the history has been walked (F-RDR-04).</summary>
[ApiController]
[Route("api/v1/conversations")]
public sealed class ConversationsController : ControllerBase
{
    /// <summary>The most conversations one call returns.</summary>
    private const int MaxLimit = 200;

    private readonly IConversationStore _conversations;
    private readonly ExtensionInbox _inbox;
    private readonly IOptions<ReaderOptions> _reader;
    private readonly AccountService _accounts;
    private readonly IAccountContext _context;

    public ConversationsController(IConversationStore conversations, ExtensionInbox inbox, IOptions<ReaderOptions> reader, AccountService accounts, IAccountContext context)
    {
        _conversations = conversations;
        _inbox = inbox;
        _reader = reader;
        _accounts = accounts;
        _context = context;
    }

    /// <summary>The conversations that were seen most recently by the account that is being read, or by the account that <paramref name="account"/> names (read-only), and the progress of the backfill of the former.</summary>
    [HttpGet]
    [ProducesResponseType<ConversationsResource>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<ConversationsResource> Get([FromQuery] int limit = 50, [FromQuery] string? account = null)
    {
        var count = Math.Clamp(limit, 1, MaxLimit);
        if (account is not null && (_context.Current is not { } current || !string.Equals(account, AccountService.IdOf(current), StringComparison.Ordinal)))
        {
            if (_accounts.KeyOf(account) is not { } key)
            {
                return Problem(statusCode: StatusCodes.Status404NotFound, title: "There is no such account.");
            }

            var other = _accounts.ConversationsOf(key);
            return new ConversationsResource(other.Count(), [.. other.Recent(count).Select(ConversationResource.From)], null);
        }

        var (list, complete) = _inbox.ListWithEnd();
        return new ConversationsResource(
            _conversations.Count(),
            [.. _conversations.Recent(count).Select(ConversationResource.From)],
            BackfillResource.From(BackfillProgress.Of(list, id => _conversations.Get(id) is not null, _reader.Value, complete)));
    }
}
