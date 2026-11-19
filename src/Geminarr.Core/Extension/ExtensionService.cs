using System.Text.RegularExpressions;
using Geminarr.Core.Browser;
using Geminarr.Core.Masking;
using Geminarr.Core.Pipeline;
using Microsoft.Extensions.Options;

namespace Geminarr.Core.Extension;

/// <summary>
/// What Geminarr does with what the extension says (ADR 0022). The extension is the one thing in the browser that talks to Geminarr, so its words are untrusted input: bounded in number and size, shaped, and never echoed
/// back. A list is answered with the conversations that Geminarr wants read (the rule of the pass, <see cref="ConversationSelection"/>), a conversation asks the pass to run.
/// </summary>
public sealed partial class ExtensionService
{
    public const int MaxPushedItems = 500;

    /// <summary>The most conversations that one answer asks the extension to open: the extension opens no more (`MAX_TABS_PER_ANSWER`), so what is asked past it would be lost. The recent ones come first, the backfill takes what is left.</summary>
    public const int MaxOpenPerAnswer = 25;

    /// <summary>What <c>last_error</c> of a conversation says when it could not be read to its beginning.</summary>
    public const string UnfinishedReason = "unfinished-read";
    public const int MaxTitleLength = 300;
    public const int MaxTurns = 2_000;
    public const int MaxTurnLength = 200_000;
    public const int MaxConversationLength = 2_000_000;
    public const int MaxVersionLength = 32;
    public const int MaxLabels = 4;

    private readonly ExtensionInbox _inbox;
    private readonly IConversationStore _conversations;
    private readonly IOutboxStore _outbox;
    private readonly PassTrigger _trigger;
    private readonly IOptions<ReaderOptions> _reader;
    private readonly IOptions<ExtensionOptions> _options;
    private readonly TimeProvider _clock;

    public ExtensionService(ExtensionInbox inbox, IConversationStore conversations, IOutboxStore outbox, PassTrigger trigger, IOptions<ReaderOptions> reader, IOptions<ExtensionOptions> options, TimeProvider clock)
    {
        _inbox = inbox;
        _conversations = conversations;
        _outbox = outbox;
        _trigger = trigger;
        _reader = reader;
        _options = options;
        _clock = clock;
    }

    /// <summary>How long a request that waits for an order is held before it is answered with nothing: shorter than the half minute after which a service worker of the browser is stopped.</summary>
    public static readonly TimeSpan ListenFor = TimeSpan.FromSeconds(25);

    // The extension keeps one such request open; a few more are allowed (a worker that restarted), no more.
    private readonly SemaphoreSlim _listeners = new(8, 8);

    /// <summary>
    /// What the extension says, answered. A <c>listen</c> is a request that waits (<see cref="ListenFor"/> at most) until the operator asks something of the extension, so that an order (a sign-out, a round) reaches the browser at once and not at the
    /// next heartbeat; everything else is answered as <see cref="Handle"/> does.
    /// </summary>
    public async Task<ExtensionPushResult> HandleAsync(ExtensionPushRequest request, CancellationToken cancellationToken)
    {
        if (request.Kind != "listen")
        {
            return Handle(request);
        }

        if (_reader.Value.Kind != ReaderKind.Web)
        {
            return ExtensionPushResult.Refused("The reader of this Geminarr is not the web one (GEMINARR__READER__KIND).");
        }

        var roundEvery = TimeSpan.FromMinutes(_options.Value.RoundMinutes);
        var wake = _inbox.Wake;
        var (round, signOut) = _inbox.TakeListenHints(_clock.GetUtcNow(), roundEvery);
        if (round || signOut)
        {
            return ExtensionPushResult.Hints(round, signOut);
        }

        // No slot is not "nothing to do": the extension is told, and waits longer before it asks again.
        if (!await _listeners.WaitAsync(0, cancellationToken))
        {
            return ExtensionPushResult.Refused("Too many requests that wait: one is enough.");
        }

        try
        {
            await Task.WhenAny(wake, Task.Delay(ListenFor, _clock, cancellationToken));
        }
        catch (OperationCanceledException)
        {
            // The extension went away: nothing is asked of it.
            return ExtensionPushResult.Hints(round: false, signOut: false);
        }
        finally
        {
            _listeners.Release();
        }

        (round, signOut) = _inbox.TakeListenHints(_clock.GetUtcNow(), roundEvery);
        return ExtensionPushResult.Hints(round, signOut);
    }

    public ExtensionPushResult Handle(ExtensionPushRequest request)
    {
        if (_reader.Value.Kind != ReaderKind.Web)
        {
            return ExtensionPushResult.Refused("The reader of this Geminarr is not the web one (GEMINARR__READER__KIND).");
        }

        var version = VersionOf(request.Version);
        try
        {
            return request.Kind switch
            {
                "state" => State(request, version),
                "list" => List(request, version),
                "conversation" => Conversation(request, version),
                "broken" => Broken(request, version),
                "unfinished" => Unfinished(request),
                "hello" => Hello(pageLoaded: false),
                "page" => Hello(pageLoaded: true),
                _ => ExtensionPushResult.Refused("Unknown kind."),
            };
        }
        catch (RegexMatchTimeoutException)
        {
            // A text that the masking could not finish is never kept: the next round brings it again.
            return ExtensionPushResult.Refused("A text could not be masked in time.");
        }
    }

    /// <summary>The extension says that it is there (<c>hello</c>, every half minute), or that a page of the assistant that a person has open has just loaded (<c>page</c>: a sign-in ends on one): it is told whether to run a round now, and whether to sign the account out. Nothing is recorded of the session by it.</summary>
    private ExtensionPushResult Hello(bool pageLoaded)
    {
        var (round, signOut) = _inbox.TakeHints(_clock.GetUtcNow(), TimeSpan.FromMinutes(_options.Value.RoundMinutes), pageLoaded);
        return ExtensionPushResult.Hints(round, signOut);
    }

    /// <summary>The labels of the account button of the page, as many as the page has links to it and no more than a few: what they say is read by <see cref="AccountLabel"/>, which bounds each of them.</summary>
    private static IReadOnlyList<string?> Labels(ExtensionPushRequest request) => request.Labels is { } labels ? [.. labels.Take(MaxLabels)] : [];

    private ExtensionPushResult State(ExtensionPushRequest request, string? version)
    {
        if (request.SignedIn is not { } signedIn)
        {
            return ExtensionPushResult.Refused("A state says whether somebody is signed in.");
        }

        var before = _inbox.Session;
        _inbox.RecordSession(signedIn, version, _clock.GetUtcNow(), Labels(request));
        // A change of who is signed in, or a page that is known again, is told at once (the page shows "sign in again" without waiting for the hour of the next pass).
        if (before?.SignedIn != signedIn || before.Broken is not null)
        {
            _trigger.Request();
        }

        return ExtensionPushResult.Ok();
    }

    /// <summary>
    /// The extension could not read a conversation to its beginning (it is too long for the time of a read, or the page stopped following the scroll): the conversation is marked in error, with a reason of ours that the page of the
    /// conversations shows, and it is not asked for again for a few hours: a failure that is a state, not a window opened at every round for nothing (ADR 0025).
    /// </summary>
    private ExtensionPushResult Unfinished(ExtensionPushRequest request)
    {
        if (!IsId(request.Id) || !_inbox.WasAsked(request.Id!))
        {
            return ExtensionPushResult.Refused("Geminarr did not ask for that conversation.");
        }

        var now = _clock.GetUtcNow();
        _inbox.RecordUnfinished(request.Id!, now);
        var known = _conversations.Get(request.Id!);
        _conversations.Save(known is null
            ? new ConversationRecord(request.Id!, null, now, now, null, null, 0, 0, null, 0, ConversationSources.Web, ConversationStates.Error, UnfinishedReason)
            : known with { State = ConversationStates.Error, LastError = UnfinishedReason, LastSeenAt = now });
        return ExtensionPushResult.Ok();
    }

    /// <summary>The extension says that the page is not what it knows, and at which of three steps: only a word of ours is kept, never what the page showed.</summary>
    private ExtensionPushResult Broken(ExtensionPushRequest request, string? version)
    {
        if (!ExtensionBrokenSteps.IsKnown(request.Step))
        {
            return ExtensionPushResult.Refused("A broken page is told at one of three steps: page, list or turns.");
        }

        var now = _clock.GetUtcNow();
        if (request.Step == ExtensionBrokenSteps.Turns)
        {
            // One conversation that shows no turn is a conversation with nothing to read; only several in a row say that the page is not what the extension knows.
            if (!IsId(request.Id))
            {
                return ExtensionPushResult.Refused("A conversation has an id of letters, digits, - and _ (64 at most).");
            }

            if (!_inbox.WasAsked(request.Id!))
            {
                return ExtensionPushResult.Refused("Geminarr did not ask for that conversation.");
            }

            if (_inbox.RecordUnreadable(request.Id!, version, now))
            {
                _trigger.Request();
            }

            return ExtensionPushResult.Ok();
        }

        _inbox.RecordBroken(request.Step!, version, now);
        _trigger.Request();
        return ExtensionPushResult.Ok();
    }

    private ExtensionPushResult List(ExtensionPushRequest request, string? version)
    {
        if (request.Items is null)
        {
            return ExtensionPushResult.Refused("A list holds items.");
        }

        if (request.Items.Count > MaxPushedItems)
        {
            return ExtensionPushResult.Refused("A list has at most " + MaxPushedItems + " conversations.");
        }

        var headers = new List<ConversationHeader>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in request.Items)
        {
            if (item is null || !IsId(item.Id))
            {
                return ExtensionPushResult.Refused("A conversation has an id of letters, digits, - and _ (64 at most).");
            }

            if (seen.Add(item.Id!))
            {
                headers.Add(new ConversationHeader(item.Id!, Title(item.Title), headers.Count));
            }
        }

        // A list is also a word about the session: the page that showed it is the page of an account (a page nobody is signed in to sends a state, not a list).
        if (_inbox.RecordList(headers, version, _clock.GetUtcNow(), Labels(request), request.Complete == true))
        {
            // Somebody signed in again, or the page is known again: the page says so at once.
            _trigger.Request();
        }

        var waiting = _outbox.ConversationsWaiting();
        // Nothing is asked for while the operator's disconnect stands (the sign-out is pending, or nobody has signed in since): what the page shows is not a session that is read.
        var disconnected = _inbox.Disconnected;
        // What is asked for is chosen among the conversations that a pass looks at (`MaxListed`), never deeper: the past is the backfill's.
        var wanted = headers
            .Take(disconnected ? 0 : _reader.Value.MaxListed)
            .Where(header => !_inbox.Has(header.Id) && !_inbox.IsUnreadable(header) && !_inbox.IsUnfinished(header.Id, _clock.GetUtcNow()) && ConversationSelection.NeedsReading(header, _conversations.Get(header.Id), waiting.Contains(header.Id)))
            .Take(_reader.Value.MaxOpenPerPass)
            .Select(header => header.Id)
            .ToList();
        // The history is walked slowly (SPEC §6.3): a few conversations that were never seen, below the ones that a pass looks at. Once they are all known nothing more is asked, so no flag says that it is done.
        if (!disconnected)
        {
            wanted.AddRange(ConversationSelection.ToBackfill(headers, _reader.Value, id => _conversations.Get(id) is not null, header => _inbox.Has(header.Id) || _inbox.IsUnreadable(header) || _inbox.IsUnfinished(header.Id, _clock.GetUtcNow())).Select(header => header.Id));
        }

        wanted = [.. wanted.Take(MaxOpenPerAnswer)];
        _inbox.Ask(wanted, _clock.GetUtcNow());
        // A conversation that Geminarr holds whole (everything it knows was sent, nothing waits) can be read from its end: the extension may stop when it finds again what Geminarr has (ADR 0025).
        return ExtensionPushResult.Ok(wanted, [.. wanted.Where(id => CanBeReadFromItsEnd(_conversations.Get(id), waiting.Contains(id)))]);
    }

    private ExtensionPushResult Conversation(ExtensionPushRequest request, string? version)
    {
        if (!IsId(request.Id))
        {
            return ExtensionPushResult.Refused("A conversation has an id of letters, digits, - and _ (64 at most).");
        }

        if (!_inbox.WasAsked(request.Id!) || _inbox.Disconnected)
        {
            return ExtensionPushResult.Refused("Geminarr did not ask for that conversation.");
        }

        if (request.Turns is not { Count: > 0 and <= MaxTurns } pushed)
        {
            return ExtensionPushResult.Refused("A conversation has between 1 and " + MaxTurns + " turns.");
        }

        var turns = new List<Turn>(pushed.Count);
        var total = 0;
        foreach (var turn in pushed)
        {
            if (turn is null || turn.Role is not ("user" or "assistant") || string.IsNullOrWhiteSpace(turn.Text) || turn.Text.Length > MaxTurnLength)
            {
                return ExtensionPushResult.Refused("A turn is spoken by the user or the assistant, and holds a text of at most " + MaxTurnLength + " characters.");
            }

            total += turn.Text.Length;
            if (total > MaxConversationLength)
            {
                return ExtensionPushResult.Refused("A conversation holds at most " + MaxConversationLength + " characters.");
            }

            // Masked before anything of it is kept (the inbox, then the outbox): a person may have pasted a secret into a conversation. The pass masks again (`PassRunner`, the one door to the outbox) on purpose: a reader that is not this one is masked too.
            turns.Add(new Turn(turn.Role, null, SecretMasker.Mask(turn.Text)));
        }

        // The end of the conversation only (`complete: false`): it is taken if it joins what Geminarr holds, and the whole conversation is asked for if it does not.
        var first = request.Complete == false ? FirstTurnOfItsEnd(request.Id!, turns) : 0;
        if (first < 0)
        {
            return ExtensionPushResult.NeedsMore();
        }

        _inbox.RecordConversation(request.Id!, Title(request.Title), turns, _clock.GetUtcNow(), version, first);
        // The pass takes it from here, without waiting for its hour.
        _trigger.Request();
        return ExtensionPushResult.Ok();
    }

    /// <summary>The position in the conversation of the first turn of an end that was handed over, found by the anchor (the last turns that were sent, which the end must hold again); -1 when it does not join what is held.</summary>
    private int FirstTurnOfItsEnd(string id, IReadOnlyList<Turn> end)
    {
        var known = _conversations.Get(id);
        if (!CanBeReadFromItsEnd(known, _outbox.ConversationsWaiting().Contains(id)))
        {
            return -1;
        }

        var length = Math.Min(ConversationLines.AnchorTurns, known!.TurnsKnown);
        var at = ConversationLines.FindAnchor(end, known.Anchor!, length);
        return at < 0 ? -1 : known.TurnsKnown - length - at;
    }

    /// <summary>Whether what Geminarr holds of a conversation lets its end be enough: it has an anchor, and all that it knows was sent (a turn that waits for Mnemarr is one that has to be read again).</summary>
    private static bool CanBeReadFromItsEnd(ConversationRecord? known, bool waiting) =>
        known is { Anchor: not null, TurnsKnown: > 0 } && known.TurnsSent == known.TurnsKnown && !waiting;

    private static string? Title(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        // Masked whole (up to a few times what is kept) and cut after: a secret that straddles the cut would keep its head in the title if the cut came first.
        var masked = SecretMasker.Mask(title.Length > MaxTitleLength * 4 ? title[..SafeEnd(title, MaxTitleLength * 4)] : title);
        return masked.Length > MaxTitleLength ? masked[..SafeEnd(masked, MaxTitleLength)] : masked;
    }

    /// <summary>Where a text may be cut at most at this length without cutting a character in two: a high surrogate at the end of the cut has lost its other half.</summary>
    private static int SafeEnd(string text, int length) => char.IsHighSurrogate(text[length - 1]) ? length - 1 : length;

    private static bool IsId(string? id) => id is not null && IdPattern().IsMatch(id);

    /// <summary>The version of the extension, for the health and the log: letters, digits and the signs of a version, and nothing that could be a sentence.</summary>
    private static string? VersionOf(string? version) => version is { Length: > 0 and <= MaxVersionLength } && VersionPattern().IsMatch(version) ? version : null;

    [GeneratedRegex("^[A-Za-z0-9_-]{1,64}$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex IdPattern();

    [GeneratedRegex(@"^[0-9A-Za-z.+-]+$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex VersionPattern();
}
