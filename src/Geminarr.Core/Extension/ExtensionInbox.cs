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

    private readonly Lock _gate = new();
    private readonly int _maxHeldCharacters;
    private readonly Dictionary<string, (string? Title, IReadOnlyList<Turn> Turns, DateTimeOffset At)> _conversations = new(StringComparer.Ordinal);
    private ExtensionSession? _session;
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
        }
    }

    /// <summary>The list as the page showed it, most recent first: a rank is a place in it. A list is also a word about the session (the page that showed it is the page of an account); says whether the session was signed out or broken before (news worth a pass).</summary>
    public bool RecordList(IReadOnlyList<ConversationHeader> list, string? version, DateTimeOffset now)
    {
        lock (_gate)
        {
            var wasNotWell = _session is { SignedIn: false } or { Broken: not null };
            _list = list;
            _session = new ExtensionSession(SignedIn: true, version, now);
            return wasNotWell;
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
            _conversations[id] = (title, turns, now);
            // The oldest go first, the one that has just arrived never: too many of them, or too much text.
            foreach (var oldest in _conversations.Where(pair => pair.Key != id).OrderBy(pair => pair.Value.At).Select(pair => pair.Key).ToList())
            {
                if (_conversations.Count <= MaxConversations && HeldCharacters() <= _maxHeldCharacters)
                {
                    break;
                }

                _conversations.Remove(oldest);
            }

            // A conversation is not a word about who is signed in: the state keeps what it was, and a page that is known again is no longer broken.
            _session = new ExtensionSession(_session?.SignedIn ?? true, _session?.Version, now);
        }
    }

    private long HeldCharacters() => _conversations.Values.Sum(held => held.Turns.Sum(turn => (long)turn.Text.Length));

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
            return _conversations.Remove(id, out var found) ? found.Turns : null;
        }
    }
}
