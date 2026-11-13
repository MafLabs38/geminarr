using Geminarr.Core.Browser;
using Geminarr.Core.Pipeline;
using Microsoft.Extensions.Options;

namespace Geminarr.Core.Connection;

/// <summary>What the operator may ask about the connection: sign in (a live view), close it, pause, resume, disconnect. The rules (one live view at a time, nothing read meanwhile) are here.</summary>
public sealed class ConnectionService
{
    private readonly ConnectionTracker _tracker;
    private readonly LiveViewService _views;
    private readonly IBrowserHost _browser;
    private readonly IOptions<ReaderOptions> _reader;
    private readonly TimeProvider _clock;

    public ConnectionService(ConnectionTracker tracker, LiveViewService views, IBrowserHost browser, IOptions<ReaderOptions> reader, TimeProvider clock)
    {
        _tracker = tracker;
        _views = views;
        _browser = browser;
        _reader = reader;
        _clock = clock;
    }

    /// <summary>Whether a browser reads this Geminarr: without it there is nothing to sign in to.</summary>
    public bool HasBrowser => _reader.Value.Kind == ReaderKind.Web;

    public ConnectionRecord Current() => _tracker.Current();

    /// <summary>Asks for a live view, where the operator signs in. Refused when no browser reads, when the passes are paused, or when a live view is open.</summary>
    public LiveViewTicket StartSignIn()
    {
        if (!HasBrowser)
        {
            throw new InvalidOperationException("No browser reads this Geminarr: set GEMINARR__READER__KIND=Web.");
        }

        if (_tracker.Current().State == ConnectionState.Paused)
        {
            throw new InvalidOperationException("The passes are paused: resume them first.");
        }

        var ticket = _views.Start();
        _tracker.Connecting();
        return ticket;
    }

    /// <summary>The live view ended: signed in (the session is made), or not (the state goes back to what the profile says).</summary>
    public void SignInEnded(bool signedIn)
    {
        if (signedIn)
        {
            _browser.MarkSignedIn();
            _tracker.Connected();
        }
        else if (_tracker.Current().State == ConnectionState.Connecting)
        {
            _tracker.SignInEnded();
        }
    }

    /// <summary>The operator closes the live view.</summary>
    public void CloseLiveView()
    {
        if (_views.Cancel() && _tracker.Current().State == ConnectionState.Connecting)
        {
            _tracker.SignInEnded();
        }
    }

    /// <summary>Closes the live view, destroys the profile (the session) and forgets the connection. What waits for Mnemarr stays.</summary>
    public async Task DisconnectAsync(CancellationToken cancellationToken)
    {
        _views.Cancel();
        await _browser.DestroyProfileAsync(cancellationToken);
        _tracker.NotConnected();
    }

    /// <summary>Quiet until a moment, or until the operator resumes.</summary>
    public void Pause(TimeSpan? duration)
    {
        if (!HasBrowser)
        {
            throw new InvalidOperationException("No browser reads this Geminarr: there is nothing to pause.");
        }

        _views.Cancel();
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
