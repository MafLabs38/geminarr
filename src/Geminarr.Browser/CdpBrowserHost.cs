using Geminarr.Core.Browser;
using Geminarr.Core.Connection;
using Geminarr.Core.Pipeline;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;

namespace Geminarr.Browser;

/// <summary>
/// The browser container (ADR 0016), reached through its debugging address. Geminarr never launches a browser: a person uses it by hand and signs in to it, and this only attaches, opens a page of its own,
/// looks at where the assistant's address ends, closes that page and lets go. It never types a credential, never changes the settings of the browser and never touches a page that it did not open.
/// One attachment at a time. Nothing of what a page holds is logged, only the kind of a failure.
/// </summary>
public sealed class CdpBrowserHost : IBrowserHost, IAsyncDisposable
{
    private readonly IOptions<BrowserOptions> _options;
    private readonly ILogger<CdpBrowserHost> _logger;
    private readonly SemaphoreSlim _attached = new(1, 1);
    private IPlaywright? _playwright;
    private volatile int _reachable = -1;

    public CdpBrowserHost(IOptions<BrowserOptions> options, ILogger<CdpBrowserHost> logger)
    {
        _options = options;
        _logger = logger;
    }

    private BrowserOptions Options => _options.Value;

    public BrowserState State() => new(!string.IsNullOrWhiteSpace(Options.CdpUrl), _reachable switch { 1 => true, 0 => false, _ => null });

    public async Task<SessionCheck> CheckSessionAsync(CancellationToken cancellationToken)
    {
        if (!State().Configured)
        {
            return SessionCheck.NoProfile;
        }

        await _attached.WaitAsync(cancellationToken);
        try
        {
            await using var browser = await AttachAsync(cancellationToken);
            return await OnTheBrowserAsync(async () =>
            {
                var page = await browser.Context.NewPageAsync();
                try
                {
                    return await LoadAsync(page, browser.Context, cancellationToken);
                }
                finally
                {
                    await CloseQuietlyAsync(page);
                }
            });
        }
        finally
        {
            _attached.Release();
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken)
    {
        if (!State().Configured)
        {
            return;
        }

        await _attached.WaitAsync(cancellationToken);
        try
        {
            await using var browser = await AttachAsync(cancellationToken);
            // The session is the cookies: they go, and nothing else of the browser is touched.
            await OnTheBrowserAsync(async () =>
            {
                await browser.Context.ClearCookiesAsync();
                return true;
            });
        }
        finally
        {
            _attached.Release();
        }
    }

    public ValueTask DisposeAsync()
    {
        _playwright?.Dispose();
        _attached.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task<Attachment> AttachAsync(CancellationToken cancellationToken)
    {
        string failure;
        try
        {
            _playwright ??= await Playwright.CreateAsync();
            var browser = await _playwright.Chromium.ConnectOverCDPAsync(Options.CdpUrl!, new BrowserTypeConnectOverCDPOptions { Timeout = Options.SessionCheckTimeoutSeconds * 1_000 }).WaitAsync(cancellationToken);
            if (browser.Contexts.Count > 0)
            {
                _reachable = 1;
                return new Attachment(browser, browser.Contexts[0]);
            }

            await browser.CloseAsync();
            failure = "no-context";
        }
        catch (Exception ex) when (ex is PlaywrightException or TimeoutException or InvalidOperationException or HttpRequestException)
        {
            failure = ex.GetType().Name;
        }

        _reachable = 0;
        // The kind of failure only: the message of a browser's error holds its address and what it was doing.
        _logger.LogWarning("The browser container could not be reached ({Failure}).", failure);
        throw new ReaderException(ConnectionReasons.BrowserFailed, "The browser container could not be reached.");
    }

    /// <summary>What a browser that drops after the attachment throws is a browser that cannot be reached, said as such and never as an unforeseen error.</summary>
    private async Task<T> OnTheBrowserAsync<T>(Func<Task<T>> work)
    {
        string failure;
        try
        {
            return await work();
        }
        catch (PlaywrightException ex)
        {
            failure = ex.GetType().Name;
        }

        _reachable = 0;
        _logger.LogWarning("The browser container dropped while it was used ({Failure}).", failure);
        throw new ReaderException(ConnectionReasons.BrowserFailed, "The browser container could not be reached.");
    }

    private async Task<SessionCheck> LoadAsync(IPage page, IBrowserContext context, CancellationToken cancellationToken)
    {
        string? failure = null;
        try
        {
            // The assistant's address only: a redirect of the assistant to the sign-in page of the account provider is what says that the session is gone.
            await page.GotoAsync(Options.AssistantUrl, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = Options.SessionCheckTimeoutSeconds * 1_000 }).WaitAsync(cancellationToken);
        }
        catch (TimeoutException)
        {
            failure = "timeout";
        }
        catch (PlaywrightException)
        {
            failure = "not-reached";
        }

        if (failure is not null)
        {
            _logger.LogWarning("The assistant's page could not be loaded ({Failure}).", failure);
            throw new ReaderException(ConnectionReasons.Unreachable, "The assistant's page could not be loaded.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        var kind = OriginPolicy.Classify(page.Url, Options);
        // The names of the cookies of the assistant's address, never their values: they say whether the account is signed in, which a guest page does not.
        var names = kind == PageKind.Assistant ? (await context.CookiesAsync([Options.AssistantUrl])).Select(cookie => cookie.Name) : [];
        return SessionVerdict.Decide(kind, SessionVerdict.HasSessionCookie(names, Options));
    }

    private static async Task CloseQuietlyAsync(IPage page)
    {
        try
        {
            await page.CloseAsync();
        }
        catch (PlaywrightException)
        {
            // The browser went away: there is nothing to close.
        }
    }

    /// <summary>One attachment: letting go of it disconnects from the browser and leaves the browser as it is.</summary>
    private sealed class Attachment : IAsyncDisposable
    {
        private readonly IBrowser _browser;

        public Attachment(IBrowser browser, IBrowserContext context)
        {
            _browser = browser;
            Context = context;
        }

        public IBrowserContext Context { get; }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await _browser.CloseAsync();
            }
            catch (PlaywrightException)
            {
                // Already gone.
            }
        }
    }
}
