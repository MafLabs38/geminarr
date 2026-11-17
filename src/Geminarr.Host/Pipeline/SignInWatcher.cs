using Geminarr.Core.Browser;
using Geminarr.Core.Pipeline;

namespace Geminarr.Host.Pipeline;

/// <summary>
/// Notices that a person has finished signing in to the browser, so that the connection says so at once instead of at the next pass (SPEC §7.2). It only reads the list of tabs of the browser (plain HTTP, nothing is attached,
/// so a sign-in is never disturbed); when a sign-in page that was open is gone, it asks for a pass, which checks the session and reads who is signed in.
/// </summary>
public sealed class SignInWatcher : BackgroundService
{
    /// <summary>How often the tabs are looked at: a person waits for the page to change.</summary>
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(4);

    private readonly IBrowserHost _browser;
    private readonly PassTrigger _trigger;
    private readonly TimeSpan _interval;

    public SignInWatcher(IBrowserHost browser, PassTrigger trigger, TimeSpan? interval = null)
    {
        _browser = browser;
        _trigger = trigger;
        _interval = interval ?? Interval;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var signingIn = false;
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(_interval, stoppingToken);
                var now = await _browser.IsSigningInAsync(stoppingToken);
                if (signingIn && !now)
                {
                    _trigger.Request();
                }

                signingIn = now;
            }
        }
        catch (OperationCanceledException)
        {
            // The application is stopping.
        }
    }
}
