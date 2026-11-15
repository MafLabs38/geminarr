using Geminarr.Core.Browser;
using Geminarr.Core.Pipeline;
using Microsoft.Extensions.Options;

namespace Geminarr.Core.Connection;

/// <summary>
/// What the operator may ask about the connection: pause, resume, disconnect. A person signs in to the browser by hand, in its own page (ADR 0016): nothing here signs in, and nothing types a credential.
/// </summary>
public sealed class ConnectionService
{
    private readonly ConnectionTracker _tracker;
    private readonly IBrowserHost _browser;
    private readonly IOptions<ReaderOptions> _reader;
    private readonly TimeProvider _clock;

    public ConnectionService(ConnectionTracker tracker, IBrowserHost browser, IOptions<ReaderOptions> reader, TimeProvider clock)
    {
        _tracker = tracker;
        _browser = browser;
        _reader = reader;
        _clock = clock;
    }

    /// <summary>Whether a browser reads this Geminarr: without it there is nothing to pause or to disconnect.</summary>
    public bool HasBrowser => _reader.Value.Kind == ReaderKind.Web;

    public ConnectionRecord Current() => _tracker.Current();

    /// <summary>Clears the session that the browser holds and forgets the connection: the person signs in again by hand. What waits for Mnemarr stays. A browser that cannot be reached is said, and the connection is forgotten all the same.</summary>
    public async Task DisconnectAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _browser.DisconnectAsync(cancellationToken);
        }
        finally
        {
            _tracker.Disconnected();
        }
    }

    /// <summary>Quiet until a moment, or until the operator resumes.</summary>
    public void Pause(TimeSpan? duration)
    {
        if (!HasBrowser)
        {
            throw new ConnectionRefusedException("No browser reads this Geminarr: there is nothing to pause.");
        }

        _tracker.Paused(duration is { } span ? _clock.GetUtcNow() + span : null);
    }

    public void Resume()
    {
        if (_tracker.Current().State == ConnectionState.Paused)
        {
            _tracker.Resumed();
        }
    }
}

/// <summary>What the operator asked cannot be done now (no browser): a sentence of ours, said as it is. Any other error is not echoed.</summary>
public sealed class ConnectionRefusedException : InvalidOperationException
{
    public ConnectionRefusedException(string message)
        : base(message)
    {
    }
}
