using Microsoft.Extensions.Options;

namespace Geminarr.Core.Browser;

/// <summary>
/// The browser container as Geminarr sees it from outside, with no debugging port (ADR 0021): it only looks whether the web desktop answers, which is where a person signs in by hand. What the session is (signed in or
/// not, as whom) is the word of the extension of the browser (ADR 0022). Nothing is attached, opened, typed or cleared.
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

    public BrowserState State() => new(!string.IsNullOrWhiteSpace(_options.Value.DesktopUrl), Volatile.Read(ref _reachable) switch { 1 => true, 0 => false, _ => null });

    public async Task<bool> ProbeAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.Value.DesktopUrl))
        {
            return true;
        }

        try
        {
            using var client = _http.CreateClient(nameof(DesktopBrowserHost));
            client.Timeout = Timeout;
            using var response = await client.GetAsync(_options.Value.DesktopUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            Volatile.Write(ref _reachable, 1);
            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            Volatile.Write(ref _reachable, 0);
            return false;
        }
    }
}
