using System.Text.RegularExpressions;
using Geminarr.Core.Pipeline;
using Microsoft.Extensions.Options;

namespace Geminarr.Core.Extension;

/// <summary>
/// What Geminarr does with what the extension says (ADR 0015). The extension is the one thing in the browser that talks to Geminarr, so its words are untrusted input: bounded in number and size, shaped, and never echoed
/// back. A list is answered with the conversations that Geminarr wants read (the rule of the pass, <see cref="ConversationSelection"/>), a conversation asks the pass to run.
/// </summary>
public sealed partial class ExtensionService
{
    public const int MaxListed = 500;
    public const int MaxTitleLength = 300;
    public const int MaxTurns = 2_000;
    public const int MaxTurnLength = 200_000;
    public const int MaxConversationLength = 2_000_000;
    public const int MaxVersionLength = 32;

    private readonly ExtensionInbox _inbox;
    private readonly IConversationStore _conversations;
    private readonly IOutboxStore _outbox;
    private readonly PassTrigger _trigger;
    private readonly IOptions<ReaderOptions> _reader;
    private readonly TimeProvider _clock;

    public ExtensionService(ExtensionInbox inbox, IConversationStore conversations, IOutboxStore outbox, PassTrigger trigger, IOptions<ReaderOptions> reader, TimeProvider clock)
    {
        _inbox = inbox;
        _conversations = conversations;
        _outbox = outbox;
        _trigger = trigger;
        _reader = reader;
        _clock = clock;
    }

    public ExtensionPushResult Handle(ExtensionPushRequest request)
    {
        if (_reader.Value.Kind != ReaderKind.Web)
        {
            return ExtensionPushResult.Refused("The reader of this Geminarr is not the web one (GEMINARR__READER__KIND).");
        }

        var version = VersionOf(request.Version);
        return request.Kind switch
        {
            "state" => State(request, version),
            "list" => List(request, version),
            "conversation" => Conversation(request),
            "broken" => Broken(request, version),
            _ => ExtensionPushResult.Refused("Unknown kind."),
        };
    }

    private ExtensionPushResult State(ExtensionPushRequest request, string? version)
    {
        if (request.SignedIn is not { } signedIn)
        {
            return ExtensionPushResult.Refused("A state says whether somebody is signed in.");
        }

        var before = _inbox.Session;
        _inbox.RecordSession(signedIn, version, _clock.GetUtcNow());
        // A change of who is signed in, or a page that is known again, is told at once (the page shows "sign in again" without waiting for the hour of the next pass).
        if (before?.SignedIn != signedIn || before.Broken is not null)
        {
            _trigger.Request();
        }

        return ExtensionPushResult.Ok();
    }

    /// <summary>The extension says that the page is not what it knows, and at which of three steps: only a word of ours is kept, never what the page showed.</summary>
    private ExtensionPushResult Broken(ExtensionPushRequest request, string? version)
    {
        if (!ExtensionBrokenSteps.IsKnown(request.Step))
        {
            return ExtensionPushResult.Refused("A broken page is told at one of three steps: page, list or turns.");
        }

        _inbox.RecordBroken(request.Step!, version, _clock.GetUtcNow());
        _trigger.Request();
        return ExtensionPushResult.Ok();
    }

    private ExtensionPushResult List(ExtensionPushRequest request, string? version)
    {
        if (request.Items is not { Count: <= MaxListed } items)
        {
            return ExtensionPushResult.Refused("A list has at most " + MaxListed + " conversations.");
        }

        var headers = new List<ConversationHeader>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            if (!IsId(item.Id))
            {
                return ExtensionPushResult.Refused("A conversation has an id of letters, digits, - and _ (64 at most).");
            }

            if (seen.Add(item.Id!))
            {
                headers.Add(new ConversationHeader(item.Id!, Title(item.Title), headers.Count));
            }
        }

        // A list is also a word about the session: the page that showed it is the page of an account (a page nobody is signed in to sends a state, not a list).
        if (_inbox.RecordList(headers, version, _clock.GetUtcNow()))
        {
            // Somebody signed in again, or the page is known again: the page says so at once.
            _trigger.Request();
        }

        var waiting = _outbox.ConversationsWaiting();
        // What is asked for is chosen among the conversations that a pass looks at (MaxListed), never deeper: the past is the backfill's.
        var wanted = headers
            .Take(_reader.Value.MaxListed)
            .Where(header => !_inbox.Has(header.Id) && ConversationSelection.NeedsReading(header, _conversations.Get(header.Id), waiting.Contains(header.Id)))
            .Take(_reader.Value.MaxOpenPerPass)
            .Select(header => header.Id)
            .ToList();
        return ExtensionPushResult.Ok(wanted);
    }

    private ExtensionPushResult Conversation(ExtensionPushRequest request)
    {
        if (!IsId(request.Id))
        {
            return ExtensionPushResult.Refused("A conversation has an id of letters, digits, - and _ (64 at most).");
        }

        if (request.Turns is not { Count: > 0 and <= MaxTurns } pushed)
        {
            return ExtensionPushResult.Refused("A conversation has between 1 and " + MaxTurns + " turns.");
        }

        var turns = new List<Turn>(pushed.Count);
        var total = 0;
        foreach (var turn in pushed)
        {
            if (turn.Role is not ("user" or "assistant") || string.IsNullOrWhiteSpace(turn.Text) || turn.Text.Length > MaxTurnLength)
            {
                return ExtensionPushResult.Refused("A turn is spoken by the user or the assistant, and holds a text of at most " + MaxTurnLength + " characters.");
            }

            total += turn.Text.Length;
            if (total > MaxConversationLength)
            {
                return ExtensionPushResult.Refused("A conversation holds at most " + MaxConversationLength + " characters.");
            }

            turns.Add(new Turn(turn.Role, null, turn.Text));
        }

        _inbox.RecordConversation(request.Id!, Title(request.Title), turns, _clock.GetUtcNow());
        // The pass takes it from here, without waiting for its hour.
        _trigger.Request();
        return ExtensionPushResult.Ok();
    }

    private static string? Title(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        return title.Length > MaxTitleLength ? title[..MaxTitleLength] : title;
    }

    private static bool IsId(string? id) => id is not null && IdPattern().IsMatch(id);

    /// <summary>The version of the extension, for the health and the log: letters, digits and the signs of a version, and nothing that could be a sentence.</summary>
    private static string? VersionOf(string? version) => version is { Length: > 0 and <= MaxVersionLength } && VersionPattern().IsMatch(version) ? version : null;

    [GeneratedRegex("^[A-Za-z0-9_-]{1,64}$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex IdPattern();

    [GeneratedRegex(@"^[0-9A-Za-z.+-]+$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex VersionPattern();
}
