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

    /// <summary>Whether the browser was sent to a sign-in page since the last check began.</summary>
    public bool SignInRedirectSeen { get; private set; }

    /// <summary>Every request goes through the policy (D-08). A page of the sign-in provider is recorded when the browser is only checking: the redirect is what says the session is gone, and nothing of that page is loaded.</summary>
    public Task FilterAsync() =>
        Context.RouteAsync("**/*", async route =>
        {
            var address = route.Request.Url;
            if (OriginPolicy.Allows(address, _options, _liveView))
            {
                await route.ContinueAsync();
                return;
            }

            if (OriginPolicy.Classify(address, _options) == PageKind.SignIn && route.Request.IsNavigationRequest)
            {
                SignInRedirectSeen = true;
            }

            await route.AbortAsync("blockedbyclient");
        });

    /// <summary>Opens the assistant's page and says whether the browser ended on it (signed in) or was sent to sign in (signed out). A page that is neither is the reader's problem, and the network is not the session's.</summary>
    public async Task<SessionCheck> CheckAsync(CancellationToken cancellationToken)
    {
        var page = Context.Pages.FirstOrDefault() ?? await Context.NewPageAsync();
        SignInRedirectSeen = false;
        try
        {
            await page.GotoAsync(_options.AssistantUrl, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = _options.SessionCheckTimeoutSeconds * 1_000 });
        }
        catch (TimeoutException ex)
        {
            _logger.LogWarning(ex, "The assistant's page did not load in time.");
            throw new ReaderException(ConnectionReasons.Unreachable, "The assistant's page did not load in time.");
        }
        catch (PlaywrightException ex) when (!SignInRedirectSeen)
        {
            _logger.LogWarning(ex, "The assistant's page could not be loaded.");
            throw new ReaderException(ex.Message.Contains("BLOCKED_BY_CLIENT", StringComparison.Ordinal) ? ConnectionReasons.ReaderBroken : ConnectionReasons.Unreachable, "The assistant's page could not be loaded.");
        }
        catch (PlaywrightException)
        {
            // The browser was sent to a sign-in page and the policy refused to load it: that is the answer.
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (SignInRedirectSeen || OriginPolicy.Classify(page.Url, _options) == PageKind.SignIn)
        {
            return SessionCheck.SignedOut;
        }

        return OriginPolicy.Classify(page.Url, _options) == PageKind.Assistant
            ? SessionCheck.SignedIn
            : throw new ReaderException(ConnectionReasons.ReaderBroken, "The assistant's page is not what the reader knows.");
    }

    public async ValueTask DisposeAsync() => await Context.CloseAsync();
}
