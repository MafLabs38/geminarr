using Geminarr.Core.Connection;
using Geminarr.Core.Pipeline;
using Microsoft.Extensions.Options;

namespace Geminarr.Core.Browser;

/// <summary>
/// The browser container as Geminarr sees it from outside, with no debugging port: it only knows whether the web desktop of the browser answers, which is where a person signs in by hand. What the session is (signed in or
/// not, as whom) is not known from here: the extension of the browser says it (ADR 0015), and until it does the connection is "not connected". Nothing is attached, opened, typed or cleared.
/// </summary>
public sealed class DesktopBrowserHost : IBrowserHost
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private readonly IOptions<BrowserOptions> _options;
    private readonly IHttpClientFactory _http;
    private int _reachable = -1;

    public DesktopBrowserHost(IOptions<BrowserOptions> options, IHttpClientFactory http)
    {
        _options = options;
        _http = http;
    }

    /// <summary>The handler of the one call that this host makes: no redirect is followed, no proxy of the environment is used, no cookie is kept (a browser container is a third party's image).</summary>
    public static HttpMessageHandler NewHandler() => new SocketsHttpHandler { AllowAutoRedirect = false, UseProxy = false, UseCookies = false };

    public BrowserState State() => new(!string.IsNullOrWhiteSpace(_options.Value.DesktopUrl), _reachable switch { 1 => true, 0 => false, _ => null });

    public async Task<SessionCheck> CheckSessionAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.Value.DesktopUrl))
        {
            return SessionCheck.NoBrowser;
        }

        try
        {
            using var client = _http.CreateClient(nameof(DesktopBrowserHost));
            client.Timeout = Timeout;
            using var response = await client.GetAsync(_options.Value.DesktopUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            _reachable = 1;
            return SessionCheck.NoBrowser;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            _reachable = 0;
            throw new ReaderException(ConnectionReasons.BrowserFailed, "The browser container could not be reached.");
        }
    }

    public Task<bool?> IsSigningInAsync(CancellationToken cancellationToken) => Task.FromResult<bool?>(null);

    public Task OpenSignInPageAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task DisconnectAsync(CancellationToken cancellationToken) =>
        throw new ConnectionRefusedException("Disconnecting comes with the extension of the browser: this version cannot clear the session.");
}
