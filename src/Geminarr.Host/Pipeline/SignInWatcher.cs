using Geminarr.Core.Browser;
using Geminarr.Core.Connection;
using Geminarr.Core.Pipeline;

namespace Geminarr.Host.Pipeline;

/// <summary>
/// Notices that a person has finished signing in to the browser, so that the connection says so at once instead of at the next pass (SPEC §7.2). It only reads the list of tabs of the browser (plain HTTP, nothing is attached,
/// so a sign-in is never disturbed); when a sign-in page that was open is gone, it asks for a pass, which checks the session and reads who is signed in.
/// </summary>
public sealed class SignInWatcher : BackgroundService
{
    /// <summary>How often the tabs are looked at: a person waits for the page to change.</summary>
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);

    private readonly IBrowserHost _browser;
    private readonly PassTrigger _trigger;
    private readonly SignInObserver _observer;
    private readonly ILogger<SignInWatcher> _logger;
    private readonly TimeSpan _interval;

    public SignInWatcher(IBrowserHost browser, PassTrigger trigger, SignInObserver observer, ILogger<SignInWatcher> logger, TimeSpan? interval = null)
    {
        _logger = logger;
        _browser = browser;
        _trigger = trigger;
        _observer = observer;
        _interval = interval ?? Interval;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var signingIn = false;
        var reachable = true;
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(_interval, stoppingToken);
                // No browser, nothing to look at.
                if (!_browser.State().Configured)
                {
                    continue;
                }

                string? failure = null;
                try
                {
                    (signingIn, reachable) = await LookAsync(signingIn, reachable, stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // What a watcher meets (a bad address, a browser that is being disposed) must not end it, and a hosted service that fails stops the whole host: it is said, by its kind only (the message of a browser's error holds its address), and it looks again.
                    failure = ex.GetType().Name;
                }

                if (failure is not null)
                {
                    _logger.LogWarning("The tabs of the browser could not be looked at ({Failure}).", failure);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // The application is stopping.
        }
    }

    /// <summary>One look at the tabs: says whether a sign-in is under way and whether the browser answers, and asks for a pass when a sign-in has just ended or a browser that was gone is back.</summary>
    private async Task<(bool SigningIn, bool Reachable)> LookAsync(bool was, bool wasReachable, CancellationToken cancellationToken)
    {
        var now = await _browser.IsSigningInAsync(cancellationToken);
        if (was && !now)
        {
            _trigger.Request();
        }

        // A browser that was gone and answers again (a restart of both containers: the first pass came before it was up) is looked at now, not after the backoff of the failed pass.
        var answers = _browser.State().Reachable != false;
        if (answers && !wasReachable)
        {
            _trigger.Request();
        }

        _observer.Set(now);
        return (now, answers);
    }
}
