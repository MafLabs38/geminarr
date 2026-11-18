using Geminarr.Core.Pipeline;

namespace Geminarr.Core.Link;

/// <summary>The sink that sends to the Mnemarr that is linked: the destination is resolved at each call, so that a pairing, an unlink or a rotated key is taken at once.</summary>
public sealed class MnemarrSink : IConversationSink
{
    private readonly LinkService _link;
    private readonly MnemarrClient _client;

    public MnemarrSink(LinkService link, MnemarrClient client)
    {
        _link = link;
        _client = client;
    }

    public Task<SinkPositions> PositionsAsync(CancellationToken cancellationToken)
    {
        var destination = _link.Resolve();
        return _client.PositionsAsync(destination.Url, destination.Key, destination.Machine, cancellationToken);
    }

    public Task<SinkResult> SendAsync(string session, long from, IReadOnlyList<string> lines, CancellationToken cancellationToken)
    {
        var destination = _link.Resolve();
        return _client.SendAsync(destination.Url, destination.Key, destination.Machine, session, from, lines, cancellationToken);
    }

    public Task HeartbeatAsync(Heartbeat heartbeat, CancellationToken cancellationToken)
    {
        var destination = _link.Resolve();
        return _client.HeartbeatAsync(destination.Url, destination.Key, heartbeat, cancellationToken);
    }
}
