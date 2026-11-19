using System.Collections.Concurrent;
using Geminarr.Core.Live;
using Microsoft.AspNetCore.SignalR;

namespace Geminarr.Host.Live;

/// <summary>
/// The channel that tells the screens that something changed (ADR 0023): a WebSocket (SignalR, with the fallbacks it has). It carries no data of a conversation, of the connection or of a run, only a topic: the screen
/// then reads what changed through the API, which asks for the session like any other route. A client that is not signed in cannot connect.
/// </summary>
public sealed class LiveHub : Hub
{
    public const string Path = "/api/v1/live";

    public const string Changed = "changed";
}

/// <summary>
/// Collects what changed and sends each distinct topic once per interval, so that a burst (a pass that writes several records) is a few messages. The last change of a burst is always sent: it waits for the next
/// interval, it is not dropped.
/// </summary>
public sealed class LiveBroadcaster : BackgroundService, ILiveNotifier
{
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(150);

    private readonly IHubContext<LiveHub> _hub;
    private readonly ILogger<LiveBroadcaster> _logger;
    private readonly TimeProvider _time;
    private readonly ConcurrentDictionary<string, byte> _changed = new(StringComparer.Ordinal);

    public LiveBroadcaster(IHubContext<LiveHub> hub, ILogger<LiveBroadcaster> logger, TimeProvider time)
    {
        _hub = hub;
        _logger = logger;
        _time = time;
    }

    public void Publish(string topic) => _changed[topic] = 0;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, _time);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await FlushAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
    }

    public async Task FlushAsync(CancellationToken cancellationToken)
    {
        foreach (var topic in _changed.Keys.ToList())
        {
            _changed.TryRemove(topic, out _);
            try
            {
                await _hub.Clients.All.SendAsync(LiveHub.Changed, topic, cancellationToken);
            }
            catch (Exception failure) when (failure is not OperationCanceledException)
            {
                // A client that went away is not the host's problem; the next change is sent as usual.
                _logger.LogDebug(failure, "A change could not be sent to the screens.");
            }
        }
    }
}
