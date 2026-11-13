using Geminarr.Core.Link;
using Geminarr.Core.Pipeline;

namespace Geminarr.Core.Test.Support;

/// <summary>A reader whose conversations are what the test says.</summary>
public sealed class FakeReader : IConversationReader
{
    public SessionCheck Session { get; set; } = SessionCheck.SignedIn;

    public Exception? FailWith { get; set; }

    public Dictionary<string, (string? Title, List<Turn> Turns)> Conversations { get; } = new(StringComparer.Ordinal);

    public List<string> Opened { get; } = [];

    public Task<SessionCheck> CheckSessionAsync(CancellationToken cancellationToken) => Task.FromResult(Session);

    public Task<IReadOnlyList<ConversationHeader>> ListRecentAsync(int limit, CancellationToken cancellationToken)
    {
        if (FailWith is not null)
        {
            throw FailWith;
        }

        IReadOnlyList<ConversationHeader> headers = [.. Conversations.Select((pair, rank) => new ConversationHeader(pair.Key, pair.Value.Title, rank)).Take(limit)];
        return Task.FromResult(headers);
    }

    public Task<IReadOnlyList<Turn>> OpenAsync(string id, CancellationToken cancellationToken)
    {
        Opened.Add(id);
        return Task.FromResult<IReadOnlyList<Turn>>([.. Conversations[id].Turns]);
    }

    public void Say(string id, string? title, params string[] texts) =>
        Conversations[id] = (title, [.. texts.Select((text, index) => new Turn(index % 2 == 0 ? "user" : "assistant", null, text))]);
}

/// <summary>A Mnemarr in memory: it holds what it was sent like the real one (it skips an overlap and refuses a hole) and remembers every call.</summary>
public sealed class FakeSink : IConversationSink
{
    public Dictionary<string, long> Held { get; } = new(StringComparer.Ordinal);

    public List<(string Session, long From, IReadOnlyList<string> Lines)> Sent { get; } = [];

    public List<Heartbeat> Heartbeats { get; } = [];

    public int PositionCalls { get; private set; }

    public SinkException? FailPositions { get; set; }

    public SinkException? FailSend { get; set; }

    public SinkException? FailHeartbeat { get; set; }

    /// <summary>Fails the sends of one session only, with this failure.</summary>
    public (string Session, SinkException Failure)? FailSession { get; set; }

    public Task<SinkPositions> PositionsAsync(CancellationToken cancellationToken)
    {
        PositionCalls++;
        return FailPositions is not null ? throw FailPositions : Task.FromResult(new SinkPositions(new Dictionary<string, long>(Held)));
    }

    public Task<SinkResult> SendAsync(string session, long from, IReadOnlyList<string> lines, CancellationToken cancellationToken)
    {
        if (FailSend is not null)
        {
            throw FailSend;
        }

        if (FailSession is { } fail && fail.Session == session)
        {
            throw fail.Failure;
        }

        var held = Held.GetValueOrDefault(session);
        if (from > held)
        {
            throw new SinkException(SinkFailure.Rejected, "Mnemarr said no (400).");
        }

        Sent.Add((session, from, lines));
        var next = Math.Max(held, from + lines.Count);
        Held[session] = next;
        return Task.FromResult(new SinkResult(next, (int)(next - held)));
    }

    public Task HeartbeatAsync(Heartbeat heartbeat, CancellationToken cancellationToken)
    {
        if (FailHeartbeat is not null)
        {
            throw FailHeartbeat;
        }

        Heartbeats.Add(heartbeat);
        return Task.CompletedTask;
    }
}

/// <summary>A key kept in memory.</summary>
public sealed class FakeKeys : IKeyStore
{
    public string? Key { get; set; }

    public string? Read() => Key;

    public void Write(string key) => Key = key;

    public void Delete() => Key = null;
}
