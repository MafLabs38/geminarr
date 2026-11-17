using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Geminarr.Core.Browser;
using Geminarr.Core.Connection;
using Geminarr.Core.Pipeline;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;

namespace Geminarr.Browser;

/// <summary>
/// The browser container (ADR 0016), reached through its debugging address. Geminarr never launches a browser: a person uses it by hand and signs in to it, and this only attaches, opens a page of its own,
/// looks at where the assistant's address ends, closes that page and lets go. It never types a credential and never changes a setting of the browser. Attaching shows every tab of the browser to the driver for the seconds of a pass, so a pass first reads the list of tabs (plain
/// HTTP, nothing attached) and steps aside while one of them is on a sign-in page: a person is signing in. One attachment at a time. Nothing of what a page holds is logged, only the kind of a failure.
/// </summary>
public sealed class CdpBrowserHost : IBrowserHost, IAsyncDisposable
{
    private readonly IOptions<BrowserOptions> _options;
    private readonly ILogger<CdpBrowserHost> _logger;
    private readonly SemaphoreSlim _attached = new(1, 1);
    private const int ListTimeoutSeconds = 5;
    private const int AccountWaitMilliseconds = 8_000;
    private static readonly HttpClient Tabs = new() { Timeout = Timeout.InfiniteTimeSpan };

    private readonly SigningInGuard _signingIn = new();
    private IPlaywright? _playwright;
    private volatile int _reachable = -1;
    private volatile AccountIdentity? _account;

    public CdpBrowserHost(IOptions<BrowserOptions> options, ILogger<CdpBrowserHost> logger)
    {
        _options = options;
        _logger = logger;
    }

    private BrowserOptions Options => _options.Value;

    public BrowserState State() => new(!string.IsNullOrWhiteSpace(Options.CdpUrl), _reachable switch { 1 => true, 0 => false, _ => null }, _account);

    public async Task<bool> IsSigningInAsync(CancellationToken cancellationToken)
    {
        if (!State().Configured)
        {
            return false;
        }

        try
        {
            return await ATabIsOnASignInPageAsync(cancellationToken, quiet: true);
        }
        catch (ReaderException)
        {
            // A browser that does not answer is not a person signing in; the passes say what is wrong with it.
            return false;
        }
    }

    public async Task<SessionCheck> CheckSessionAsync(CancellationToken cancellationToken)
    {
        if (!State().Configured)
        {
            return SessionCheck.NoBrowser;
        }

        await _attached.WaitAsync(cancellationToken);
        try
        {
            // Attaching makes a program of every tab of the browser for a moment: not while a person is typing on a sign-in page.
            if (_signingIn.StepsAside(await ATabIsOnASignInPageAsync(cancellationToken), Options.SigningInPasses))
            {
                return SessionCheck.SigningIn;
            }

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
            _account = null;
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

    /// <summary>
    /// Whether a tab of the browser is on a sign-in page, read from the list of tabs that the debugging port serves over plain HTTP: nothing is attached to read it. A browser that does not answer is not a person
    /// signing in (the attachment says what is wrong).
    /// </summary>
    private async Task<bool> ATabIsOnASignInPageAsync(CancellationToken cancellationToken, bool quiet = false)
    {
        string failure;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(ListTimeoutSeconds));
            var tabs = await Tabs.GetFromJsonAsync<List<TabInfo>>(new Uri(new Uri(Options.CdpUrl!), "/json/list"), timeout.Token);
            return SessionVerdict.APersonIsSigningIn((tabs ?? []).Select(tab => new BrowserTab(tab.Type, tab.Url)), Options);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            failure = ex.GetType().Name;
        }

        // A browser that cannot even list its tabs cannot be attached to either, and a guard that failed open would attach in the middle of a sign-in: the pass says the browser failed.
        _reachable = 0;
        if (!quiet)
        {
            _logger.LogWarning("The list of the tabs of the browser could not be read ({Failure}).", failure);
        }

        throw new ReaderException(ConnectionReasons.BrowserFailed, "The browser container could not be reached.");
    }

    private sealed record TabInfo([property: JsonPropertyName("type")] string? Type, [property: JsonPropertyName("url")] string? Url);

    private async Task<Attachment> AttachAsync(CancellationToken cancellationToken)
    {
        string failure;
        try
        {
            _playwright ??= await Playwright.CreateAsync();
            var connecting = _playwright.Chromium.ConnectOverCDPAsync(Options.CdpUrl!, new BrowserTypeConnectOverCDPOptions { Timeout = Options.SessionCheckTimeoutSeconds * 1_000 });
            IBrowser browser;
            try
            {
                browser = await connecting.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // The pass gave up: a connection that completes later is closed, not left open on the browser.
                _ = CloseWhenConnectedAsync(connecting);
                throw;
            }

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
        var check = SessionVerdict.Decide(kind, SessionVerdict.HasSessionCookie(names, Options));
        // Who it is signed in as is read from the page that was just loaded; a signed-out session has nobody.
        _account = check == SessionCheck.SignedIn ? await ReadAccountAsync(page) ?? _account : null;
        return check;
    }

    /// <summary>
    /// The label of the account button of the assistant's page (a link to the page of the account provider where one signs out), parsed by <see cref="AccountLabel"/>. The page may not have drawn it yet or may not show one:
    /// that is no account known, never an error of the session. Never logged.
    /// </summary>
    private static async Task<AccountIdentity?> ReadAccountAsync(IPage page)
    {
        const string selector = "a[href*='SignOutOptions']";
        try
        {
            await page.WaitForSelectorAsync(selector, new PageWaitForSelectorOptions { State = WaitForSelectorState.Attached, Timeout = AccountWaitMilliseconds });
            var labels = await page.Locator(selector).EvaluateAllAsync<string[]>("elements => elements.slice(0, 4).map(element => element.getAttribute('aria-label') || '')");
            return AccountLabel.Parse(labels);
        }
        catch (Exception ex) when (ex is TimeoutException or PlaywrightException)
        {
            return null;
        }
    }

    private static async Task CloseWhenConnectedAsync(Task<IBrowser> connecting)
    {
        try
        {
            await (await connecting).CloseAsync();
        }
        catch (Exception ex) when (ex is PlaywrightException or TimeoutException)
        {
            // It never connected: there is nothing to close.
        }
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
