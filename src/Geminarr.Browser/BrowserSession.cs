using Geminarr.Core.Browser;
using Geminarr.Core.Connection;
using Geminarr.Core.Pipeline;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace Geminarr.Browser;

/// <summary>One launch of the browser on the profile: a route filter that refuses every origin the policy does not allow, and the check of the session.</summary>
internal sealed class BrowserSession : IAsyncDisposable
{
    private readonly BrowserOptions _options;
    private readonly bool _liveView;
    private readonly ILogger _logger;

    public BrowserSession(IBrowserContext context, BrowserOptions options, bool liveView, ILogger logger)
    {
        Context = context;
        _options = options;
        _liveView = liveView;
        _logger = logger;
    }

    public IBrowserContext Context { get; }

    // Written by the route handler (a thread of the browser's) and read by the check: volatile.
    private volatile bool _signInRedirectSeen;
    private volatile bool _blockedSeen;

    /// <summary>Whether the browser was sent to a sign-in page since the last check began.</summary>
    public bool SignInRedirectSeen => _signInRedirectSeen;

    /// <summary>Every request goes through the policy (D-08). A page of the sign-in provider is recorded when the browser is only checking: the redirect is what says the session is gone, and nothing of that page is loaded.</summary>
    public async Task FilterAsync()
    {
        await Context.RouteAsync("**/*", async route =>
        {
            var address = route.Request.Url;
            if (OriginPolicy.Allows(address, _options, _liveView, route.Request.IsNavigationRequest))
            {
                await route.ContinueAsync();
                return;
            }

            if (OriginPolicy.Classify(address, _options) == PageKind.SignIn && route.Request.IsNavigationRequest)
            {
                _signInRedirectSeen = true;
            }
            else if (route.Request.IsNavigationRequest)
            {
                _blockedSeen = true;
            }

            await route.AbortAsync("blockedbyclient");
        });

        // A WebSocket is not a request that the route above sees: a page that may load could open one to anywhere.
        await Context.RouteWebSocketAsync("**/*", route =>
        {
            if (OriginPolicy.AllowsWebSocket(route.Url, _options, _liveView))
            {
                route.ConnectToServer();
                return;
            }

            _ = route.CloseAsync();
        });
    }

    /// <summary>Opens the assistant's page and says whether the browser ended on it (signed in) or was sent to sign in (signed out). A page that is neither is the reader's problem, and the network is not the session's.</summary>
    public async Task<SessionCheck> CheckAsync(CancellationToken cancellationToken)
    {
        var page = Context.Pages.FirstOrDefault() ?? await Context.NewPageAsync();
        _signInRedirectSeen = false;
        _blockedSeen = false;
        string? failure = null;
        try
        {
            // The stopping of the application ends the wait; the session is closed by whoever disposes it, which ends the page.
            await page.GotoAsync(_options.AssistantUrl, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = _options.SessionCheckTimeoutSeconds * 1_000 }).WaitAsync(cancellationToken);
        }
        catch (TimeoutException)
        {
            failure = "timeout";
        }
        catch (PlaywrightException) when (!_signInRedirectSeen)
        {
            failure = _blockedSeen ? "refused-by-the-policy" : "not-reached";
        }
        catch (PlaywrightException)
        {
            // The browser was sent to a sign-in page and the policy refused to load it: that is the answer.
        }

        if (failure is not null)
        {
            // The kind of failure only: the message of a browser's error holds its command line and the addresses it was on.
            _logger.LogWarning("The assistant's page could not be loaded ({Failure}).", failure);
            throw new ReaderException(failure == "refused-by-the-policy" ? ConnectionReasons.ReaderBroken : ConnectionReasons.Unreachable, "The assistant's page could not be loaded.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (_signInRedirectSeen || OriginPolicy.Classify(page.Url, _options) == PageKind.SignIn)
        {
            return SessionCheck.SignedOut;
        }

        return OriginPolicy.Classify(page.Url, _options) == PageKind.Assistant
            ? SessionCheck.SignedIn
            : throw new ReaderException(ConnectionReasons.ReaderBroken, "The assistant's page is not what the reader knows.");
    }

    public async ValueTask DisposeAsync() => await Context.CloseAsync();
}
