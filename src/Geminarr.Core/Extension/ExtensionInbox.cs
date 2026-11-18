using Geminarr.Core.Pipeline;

namespace Geminarr.Core.Extension;

/// <summary>What the extension said last about the session: signed in or not, with which version of itself, and when.</summary>
public sealed record ExtensionSession(bool SignedIn, string? Version, DateTimeOffset At);

/// <summary>
/// What the extension has handed over and the next pass has not taken yet, in memory (SPEC §6, D-06): the list of conversations as the page showed it, and the turns of the conversations that it read. A conversation is
/// <b>taken</b> by the pass that reads it, so that no text stays here once it is on its way to Mnemarr: Geminarr keeps no second copy, and a restart loses only what the extension brings again at its next round.
/// </summary>
public sealed class ExtensionInbox
{
    /// <summary>More conversations than a list shows: a limit on what a misbehaving extension could make this hold.</summary>
    public const int MaxConversations = 200;

    private readonly Lock _gate = new();
    private readonly Dictionary<string, (string? Title, IReadOnlyList<Turn> Turns, DateTimeOffset At)> _conversations = new(StringComparer.Ordinal);
    private ExtensionSession? _session;
    private IReadOnlyList<ConversationHeader> _list = [];

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

    /// <summary>The list as the page showed it, most recent first: a rank is a place in it.</summary>
    public void RecordList(IReadOnlyList<ConversationHeader> list, DateTimeOffset now)
    {
        lock (_gate)
        {
            _list = list;
            _session = _session is null ? new ExtensionSession(true, null, now) : _session with { At = now };
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
            foreach (var oldest in _conversations.OrderBy(pair => pair.Value.At).Take(Math.Max(_conversations.Count - MaxConversations, 0)).Select(pair => pair.Key).ToList())
            {
                _conversations.Remove(oldest);
            }

            _session = _session is null ? new ExtensionSession(true, null, now) : _session with { At = now };
        }
    }

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
