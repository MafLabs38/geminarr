using Geminarr.Core.Pipeline;

namespace Geminarr.Core.Extension;

/// <summary>What the extension said last about the session: signed in or not, with which version of itself, and when.</summary>
/// <remarks><paramref name="Broken"/> is the step at which the page was not what the extension knows (<see cref="ExtensionBrokenSteps"/>), until the next thing it reads well.</remarks>
public sealed record ExtensionSession(bool SignedIn, string? Version, DateTimeOffset At, string? Broken = null);

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
    private readonly Dictionary<string, DateTimeOffset> _asked = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTimeOffset> _unreadable = new(StringComparer.Ordinal);
    private IReadOnlyList<ConversationHeader> _list = [];

    public ExtensionInbox() : this(MaxHeldCharacters)
    {
    }

    internal ExtensionInbox(int maxHeldCharacters)
    {
        _maxHeldCharacters = maxHeldCharacters;
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

    public void RecordSession(bool signedIn, string? version, DateTimeOffset now)
    {
        lock (_gate)
        {
            _session = new ExtensionSession(signedIn, version, now);
            if (!signedIn)
            {
                // Nobody is signed in: what was read for the account that was is not kept for the next one.
                _conversations.Clear();
                _asked.Clear();
                _unreadable.Clear();
                _list = [];
            }
        }
    }

    /// <summary>The list as the page showed it, most recent first: a rank is a place in it. A list is also a word about the session (the page that showed it is the page of an account); says whether the session was signed out or broken at the page or the list before (news worth a pass); a list does not mend the turns.</summary>
    public bool RecordList(IReadOnlyList<ConversationHeader> list, string? version, DateTimeOffset now)
    {
        lock (_gate)
        {
            // A good list proves that the page and the list are what the extension knows, not that the turns are: that is for a conversation that is read.
            var turnsBroken = _session?.Broken == ExtensionBrokenSteps.Turns;
            var wasNotWell = _session is { SignedIn: false } or { Broken: ExtensionBrokenSteps.Page or ExtensionBrokenSteps.List };
            _list = list;
            _session = new ExtensionSession(SignedIn: true, version, now, turnsBroken ? ExtensionBrokenSteps.Turns : null);
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
            _unreadable[id] = now;
            foreach (var oldest in _unreadable.OrderBy(pair => pair.Value).Select(pair => pair.Key).Take(Math.Max(_unreadable.Count - MaxConversations, 0)).ToList())
            {
                _unreadable.Remove(oldest);
            }

            if (_unreadable.Count < UnreadableToBreak)
            {
                return false;
            }

            _session = new ExtensionSession(_session?.SignedIn ?? true, version, now, ExtensionBrokenSteps.Turns);
            return true;
        }
    }

    public bool IsUnreadable(string id)
    {
        lock (_gate)
        {
            return _unreadable.ContainsKey(id);
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

        foreach (var old in _unreadable.Where(pair => now - pair.Value > HeldFor).Select(pair => pair.Key).ToList())
        {
            _unreadable.Remove(old);
        }
    }

    /// <summary>The page is not what the extension knows, at this step: the session stays what it was said to be, with the step.</summary>
    public void RecordBroken(string step, string? version, DateTimeOffset now)
    {
        lock (_gate)
        {
            _session = new ExtensionSession(_session?.SignedIn ?? true, version, now, step);
        }
    }

    public IReadOnlyList<ConversationHeader> List()
    {
        lock (_gate)
        {
            return _list;
        }
    }

    public void RecordConversation(string id, string? title, IReadOnlyList<Turn> turns, DateTimeOffset now)
    {
        lock (_gate)
        {
            Purge(now);
            _unreadable.Clear();
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
            _session = new ExtensionSession(_session?.SignedIn ?? true, _session?.Version, now);
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
