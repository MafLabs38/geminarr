using Geminarr.Core.Browser;
using Geminarr.Core.Connection;
using Geminarr.Core.Live;
using Geminarr.Core.Pipeline;

namespace Geminarr.Core.Extension;

/// <summary>What the extension said last about the session: signed in or not, with which version of itself, and when.</summary>
/// <remarks><paramref name="Account"/> is who the page says is signed in (read from the label of its account button, in memory only, never logged and never sent to Mnemarr). <paramref name="Broken"/> is the step at which the page was not what the extension knows (<see cref="ExtensionBrokenSteps"/>), until the next thing it reads well.</remarks>
public sealed record ExtensionSession(bool SignedIn, string? Version, DateTimeOffset At, string? Broken = null, AccountIdentity? Account = null);

/// <summary>The steps at which the extension can say that the page is not what it knows: a closed list, so that nothing a page said can come through as a step.</summary>
public static class ExtensionBrokenSteps
{
    public const string Page = "page";
    public const string List = "list";
    public const string Turns = "turns";

    public static bool IsKnown(string? step) => step is Page or List or Turns;
}

/// <summary>
/// What the extension has handed over and the next pass has not taken yet, in memory (SPEC §6, D-06): the list of conversations as the page showed it, and the turns of the conversations that it read. A conversation is
/// <b>taken</b> by the pass that reads it, so that no text stays here once it is on its way to Mnemarr: Geminarr keeps no second copy, and a restart loses only what the extension brings again at its next round.
/// </summary>
public sealed class ExtensionInbox
{
    /// <summary>More conversations than a list shows: a limit on what a misbehaving extension could make this hold.</summary>
    public const int MaxConversations = 200;

    /// <summary>The text that waits for a pass, in characters, at most: the container is small, and what is dropped is brought again by the next round.</summary>
    public const int MaxHeldCharacters = 20_000_000;

    /// <summary>How many conversations in a row that show no turn make the reader broken: one or two are a conversation without text (an image, a voice), not a page that changed.</summary>
    public const int UnreadableToBreak = 3;

    /// <summary>How long what the extension handed over, or what it was asked for, is kept when no pass takes it: the next round brings it again.</summary>
    public static readonly TimeSpan HeldFor = TimeSpan.FromHours(1);

    private readonly Lock _gate = new();
    private readonly int _maxHeldCharacters;
    private readonly Dictionary<string, (string? Title, IReadOnlyList<Turn> Turns, DateTimeOffset At)> _conversations = new(StringComparer.Ordinal);
    private ExtensionSession? _session;
    private DateTimeOffset? _lastHello;
    private DateTimeOffset? _firstHello;
    private DateTimeOffset? _lastPausedHello;
    private TaskCompletionSource _wake = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Func<bool> _isPaused = static () => false;
    private readonly Action<Func<bool>>? _signOutSettled;
    private readonly ILiveNotifier _live = new NoLiveNotifier();
    private readonly ExtensionIntents _intents = new();
    private readonly Dictionary<string, DateTimeOffset> _asked = new(StringComparer.Ordinal);
    private readonly UnreadableLedger _unreadable = new(MaxConversations, HeldFor);
    private IReadOnlyList<ConversationHeader> _list = [];
    private bool _listComplete;

    public ExtensionInbox() : this(MaxHeldCharacters)
    {
    }

    /// <summary>An inbox that asks the connection whether the reading is paused (by the operator, or by a block): no round is asked of the extension then, so that the page of the assistant is not visited for nobody.</summary>
    public ExtensionInbox(Func<bool> isPaused) : this(MaxHeldCharacters)
    {
        _isPaused = isPaused;
    }

    private ExtensionInbox(Func<bool> isPaused, Action<Func<bool>>? signOutSettled, ILiveNotifier? live) : this(isPaused)
    {
        _signOutSettled = signOutSettled;
        _live = live ?? new NoLiveNotifier();
    }

    /// <summary>The inbox of a Geminarr that starts: a disconnect that the extension had not confirmed when it stopped (the record says so) is asked again.</summary>
    public static ExtensionInbox Restored(ConnectionRecord record, Func<bool>? isPaused = null, Action<Func<bool>>? signOutSettled = null, ILiveNotifier? live = null)
    {
        var inbox = new ExtensionInbox(isPaused ?? (static () => false), signOutSettled, live);
        // A pause keeps what the connection was before it: a sign-out that was not confirmed is still asked for after a restart in the middle of one.
        if (record.IsDisconnected)
        {
            lock (inbox._gate)
            {
                inbox._intents.RestoreDisconnected(record.SignOutPending);
            }
        }

        return inbox;
    }

    internal ExtensionInbox(int maxHeldCharacters)
    {
        _maxHeldCharacters = maxHeldCharacters;
    }

    /// <summary>The last time that the extension said that it is there, whatever it said of the page: it tells a browser that is off or cannot reach Geminarr from an assistant's page that does not answer.</summary>
    public DateTimeOffset? LastHello
    {
        get
        {
            lock (_gate)
            {
                return _lastHello;
            }
        }
    }

    /// <summary>The first time that the extension said that it is there since this start: the silence about a page that was never heard is measured from it.</summary>
    public DateTimeOffset? FirstHello
    {
        get
        {
            lock (_gate)
            {
                return _firstHello;
            }
        }
    }

    /// <summary>The last time that the extension said that it is there while the reading was paused: no round was asked of it then, so its silence about the page starts when the pause ends, not before.</summary>
    public DateTimeOffset? LastPausedHello
    {
        get
        {
            lock (_gate)
            {
                return _lastPausedHello;
            }
        }
    }

    public ExtensionSession? Session
    {
        get
        {
            lock (_gate)
            {
                return _session;
            }
        }
    }

    public void RecordSession(bool signedIn, string? version, DateTimeOffset now, IReadOnlyList<string?>? labels = null)
    {
        var settled = false;
        lock (_gate)
        {
            var wasPending = _intents.SignOutPending;
            _session = new ExtensionSession(signedIn, version, now, Account: signedIn ? AccountLabel.Parse(labels ?? []) : null);
            if (signedIn)
            {
                _intents.SaidSignedIn();
            }
            else
            {
                // The sign-out that was asked for is done (or was never needed): nothing is asked of the extension any longer.
                _intents.SaidSignedOut();
                // Nobody is signed in: what was read for the account that was is not kept for the next one.
                ForgetWhatWasRead();
            }

            settled = wasPending && !_intents.SignOutPending;
        }

        // Told outside the lock: the connection has a lock of its own.
        if (settled)
        {
            _signOutSettled?.Invoke(() => SignOutPending);
        }

        // Who is signed in, or that nobody is, is what the page of the connection shows.
        _live.Publish(LiveTopics.Connection);
    }

    /// <summary>The list as the page showed it, most recent first: a rank is a place in it. A list is also a word about the session (the page that showed it is the page of an account).</summary>
    /// <returns>Whether the session was signed out or broken at the page or the list before: news worth a pass. A list does not mend a break of the turns.</returns>
    public bool RecordList(IReadOnlyList<ConversationHeader> list, string? version, DateTimeOffset now, IReadOnlyList<string?>? labels = null, bool complete = false)
    {
        lock (_gate)
        {
            // A good list proves that the page and the list are what the extension knows, not that the turns are: that is for a conversation that is read.
            var turnsBroken = _session?.Broken == ExtensionBrokenSteps.Turns;
            var wasNotWell = _session is { SignedIn: false } or { Broken: ExtensionBrokenSteps.Page or ExtensionBrokenSteps.List };
            _list = list;
            _listComplete = complete;
            _session = new ExtensionSession(SignedIn: true, version, now, turnsBroken ? ExtensionBrokenSteps.Turns : null, AccountLabel.Parse(labels ?? []) ?? _session?.Account);
            _intents.SaidSignedIn();
            return wasNotWell;
        }
    }

    /// <summary>The conversations that were asked of the extension: only these are taken when it hands them over, so that nothing that was not asked for is held.</summary>
    public void Ask(IEnumerable<string> ids, DateTimeOffset now)
    {
        lock (_gate)
        {
            Purge(now);
            foreach (var id in ids)
            {
                _asked[id] = now;
            }

            // The same bound as what is held: the oldest questions go first.
            foreach (var oldest in _asked.OrderBy(pair => pair.Value).Select(pair => pair.Key).Take(Math.Max(_asked.Count - MaxConversations, 0)).ToList())
            {
                _asked.Remove(oldest);
            }
        }
    }

    public bool WasAsked(string id)
    {
        lock (_gate)
        {
            return _asked.ContainsKey(id);
        }
    }

    /// <summary>A conversation that showed no turn. Three of them in a row, with none read in between, are a page that is not what the extension knows; fewer are conversations with nothing to read, and are not asked for again for an hour.</summary>
    public bool RecordUnreadable(string id, string? version, DateTimeOffset now)
    {
        lock (_gate)
        {
            Purge(now);
            _asked.Remove(id);
            var brokenBefore = _session?.Broken == ExtensionBrokenSteps.Turns;
            if (!_unreadable.Record(id, _list.FirstOrDefault(candidate => candidate.Id == id), version, now, brokenBefore, UnreadableToBreak))
            {
                return false;
            }

            _session = new ExtensionSession(_session?.SignedIn ?? true, version, now, ExtensionBrokenSteps.Turns, _session?.Account);
            return true;
        }
    }

    /// <summary>Whether this conversation showed nothing and is still as it was, for the version of the extension that is running: the same title, and not moved up the list (a conversation that was continued comes back to the top).</summary>
    public bool IsUnreadable(ConversationHeader header)
    {
        lock (_gate)
        {
            return _unreadable.Holds(header, _session?.Version);
        }
    }

    private void Purge(DateTimeOffset now)
    {
        foreach (var old in _conversations.Where(pair => now - pair.Value.At > HeldFor).Select(pair => pair.Key).ToList())
        {
            _conversations.Remove(old);
        }

        foreach (var old in _asked.Where(pair => now - pair.Value > HeldFor).Select(pair => pair.Key).ToList())
        {
            _asked.Remove(old);
        }

        _unreadable.Purge(now);
    }

    /// <summary>The page is not what the extension knows, at this step: the session stays what it was said to be, with the step.</summary>
    public void RecordBroken(string step, string? version, DateTimeOffset now)
    {
        lock (_gate)
        {
            _session = new ExtensionSession(_session?.SignedIn ?? true, version, now, step, _session?.Account);
        }

        _live.Publish(LiveTopics.Status);
    }

    /// <summary>What was read for an account is not kept for the next one (nobody is signed in, or the operator disconnected).</summary>
    private void ForgetWhatWasRead()
    {
        _conversations.Clear();
        _asked.Clear();
        _unreadable.Clear();
        _list = [];
        _listComplete = false;
    }

    /// <summary>The operator pressed *Sign in*: a person is about to sign in to the browser, so the extension is asked for rounds, quickly, until it says that somebody is signed in (or <see cref="ExtensionIntents.SigningInFor"/> is over).</summary>
    public void BeginSigningIn(DateTimeOffset now)
    {
        lock (_gate)
        {
            _intents.BeginSigningIn(now);
            Signal();
        }
    }

    /// <summary>Completes when the operator asks something of the extension (a sign-in, a sign-out, a round, or gives one up): a request that waits for an order is answered at once. Taken before the hints are, so that no change is missed in between.</summary>
    public Task Wake
    {
        get
        {
            lock (_gate)
            {
                return _wake.Task;
            }
        }
    }

    private void Signal()
    {
        var woken = _wake;
        _wake = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        woken.TrySetResult();
    }

    /// <summary>What an extension that waits for an order is told now (see <see cref="ExtensionIntents.TakeListenHints"/>).</summary>
    public (bool Round, bool SignOut) TakeListenHints(DateTimeOffset now, TimeSpan roundEvery)
    {
        // Asked before the lock: the connection has a lock of its own.
        var paused = _isPaused();
        lock (_gate)
        {
            _lastHello = now;
            _firstHello ??= now;
            if (paused)
            {
                _lastPausedHello = now;
            }

            return _intents.TakeListenHints(now, _session, roundEvery, paused);
        }
    }

    /// <summary>Whether a sign-in that the operator started is being waited for: it ends when the extension says that somebody is signed in, and after ten minutes.</summary>
    public bool SigningIn(DateTimeOffset now)
    {
        lock (_gate)
        {
            return _intents.SigningIn(now, _session);
        }
    }

    /// <summary>The operator cancels a pending sign-out: nothing more is asked of the extension.</summary>
    public void CancelSignOut()
    {
        lock (_gate)
        {
            _intents.CancelSignOut();
            Signal();
        }
    }

    /// <summary>The operator asked to look again: the extension is asked for a round the next time it says that it is there.</summary>
    public void RequestRound()
    {
        lock (_gate)
        {
            _intents.RequestRound();
            Signal();
        }
    }

    /// <summary>The operator disconnected: the extension is asked to sign the account out (until it says that nobody is signed in), and until somebody signs in again nothing is made of the session being gone (it is what was asked for).</summary>
    public void RequestSignOut()
    {
        lock (_gate)
        {
            _intents.RequestSignOut();
            ForgetWhatWasRead();
            Signal();
        }
    }

    /// <summary>Whether the operator disconnected and nobody has signed in since the sign-out was over: while it is, whatever the page was said to show is not the session.</summary>
    public bool Disconnected
    {
        get
        {
            lock (_gate)
            {
                return _intents.Disconnected;
            }
        }
    }

    /// <summary>Whether the extension was asked to sign the account out and has not said yet that nobody is signed in (the browser may be off).</summary>
    public bool SignOutPending
    {
        get
        {
            lock (_gate)
            {
                return _intents.SignOutPending;
            }
        }
    }

    /// <summary>What the extension is asked when it says that it is there: a round now, and the sign-out (see <see cref="ExtensionIntents.TakeHints"/>).</summary>
    public (bool Round, bool SignOut) TakeHints(DateTimeOffset now, TimeSpan roundEvery, bool pageLoaded = false)
    {
        // Asked before the lock: the connection has a lock of its own.
        var paused = _isPaused();
        lock (_gate)
        {
            _lastHello = now;
            _firstHello ??= now;
            if (paused)
            {
                _lastPausedHello = now;
            }

            return _intents.TakeHints(now, _session, roundEvery, paused, pageLoaded);
        }
    }

    public IReadOnlyList<ConversationHeader> List()
    {
        lock (_gate)
        {
            return _list;
        }
    }

    /// <summary>The list and whether it ends there, read together: two reads could describe two lists.</summary>
    public (IReadOnlyList<ConversationHeader> List, bool Complete) ListWithEnd()
    {
        lock (_gate)
        {
            return (_list, _listComplete);
        }
    }

    /// <summary>Whether the page said that its list ends there (it stopped growing when scrolled), and not that the extension stopped scrolling: the end of the backfill is told by this.</summary>
    public bool ListComplete
    {
        get
        {
            lock (_gate)
            {
                return _listComplete;
            }
        }
    }

    public void RecordConversation(string id, string? title, IReadOnlyList<Turn> turns, DateTimeOffset now, string? version = null)
    {
        lock (_gate)
        {
            Purge(now);
            // A conversation was read: the run of unreadable ones starts again, each of them stays marked, and what a broken page held aside is believed if this version read it.
            _unreadable.Read(version);
            _conversations[id] = (title, turns, now);
            // The oldest go first, the one that has just arrived never: too many of them, or too much text.
            var held = HeldCharacters();
            foreach (var oldest in _conversations.Where(pair => pair.Key != id).OrderBy(pair => pair.Value.At).Select(pair => pair.Key).ToList())
            {
                if (_conversations.Count <= MaxConversations && held <= _maxHeldCharacters)
                {
                    break;
                }

                held -= CharactersOf(_conversations[oldest].Turns);
                _conversations.Remove(oldest);
            }

            // A conversation is not a word about who is signed in: the state keeps what it was, and a page that is known again is no longer broken.
            _session = new ExtensionSession(_session?.SignedIn ?? true, _session?.Version, now, Account: _session?.Account);
        }
    }

    private long HeldCharacters() => _conversations.Values.Sum(held => CharactersOf(held.Turns));

    private static long CharactersOf(IReadOnlyList<Turn> turns) => turns.Sum(turn => (long)turn.Text.Length);

    public bool Has(string id)
    {
        lock (_gate)
        {
            return _conversations.ContainsKey(id);
        }
    }

    /// <summary>The turns of a conversation, which leave the inbox with them; null when the extension has not brought it.</summary>
    public IReadOnlyList<Turn>? Take(string id)
    {
        lock (_gate)
        {
            _asked.Remove(id);
            return _conversations.Remove(id, out var found) ? found.Turns : null;
        }
    }
}
