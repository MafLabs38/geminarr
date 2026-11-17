using System.Text.Json;
using System.Text.Json.Serialization;
using Geminarr.Core.Browser;

namespace Geminarr.Browser;

/// <summary>What the list of the tabs of the browser said: the tabs, or the kind of the failure (a name, never a message: what a browser's error says holds its address).</summary>
public sealed record TabsResult(IReadOnlyList<BrowserTab>? Tabs, string? Failure);

/// <summary>
/// The half of the browser's debugging address that is plain HTTP (ADR 0016): the list of the tabs, and a tab that a person will use to sign in. Nothing is attached, so the browser sees nothing of a driver. It is a class
/// of its own so that it is tested without a browser; the half that drives Chromium is <see cref="CdpBrowserHost"/>. The debugging address is the loopback: no proxy of the environment is asked, and no redirect is followed.
/// </summary>
public sealed class CdpTabsClient : IDisposable
{
    /// <summary>The most that the list of the tabs may weigh: each tab's address is its page's, and a page can make it as long as it likes.</summary>
    public const int MaxListBytes = 1024 * 1024;

    private readonly HttpClient _http;

    public CdpTabsClient()
        : this(new HttpClient(new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan })
    {
    }

    public CdpTabsClient(HttpClient http) => _http = http;

    public void Dispose() => _http.Dispose();

    /// <summary>The tabs of the browser. A browser that does not answer, answers something else, or takes longer than <paramref name="timeoutSeconds"/> is a failure of a kind; the caller's own cancellation is not.</summary>
    public async Task<TabsResult> ListAsync(string cdpUrl, int timeoutSeconds, CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
            using var response = await _http.GetAsync(new Uri(new Uri(cdpUrl), "/json/list"), HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            response.EnsureSuccessStatusCode();
            await using var body = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var buffer = new MemoryStream();
            var chunk = new byte[8192];
            int read;
            while ((read = await body.ReadAsync(chunk, timeout.Token)) > 0)
            {
                if (buffer.Length + read > MaxListBytes)
                {
                    return new TabsResult(null, "TooLarge");
                }

                await buffer.WriteAsync(chunk.AsMemory(0, read), timeout.Token);
            }

            buffer.Position = 0;
            var tabs = await JsonSerializer.DeserializeAsync<List<TabInfo>>(buffer, JsonSerializerOptions.Web, timeout.Token);
            return new TabsResult((tabs ?? []).Select(tab => new BrowserTab(tab.Type, tab.Url)).ToList(), null);
        }
        catch (Exception ex) when (IsAFailureOfTheBrowser(ex, cancellationToken))
        {
            return new TabsResult(null, ex.GetType().Name);
        }
    }

    /// <summary>
    /// Opens a tab on <paramref name="address"/>, the browser's own way, over its plain HTTP address. The address is deliberately not escaped: the browser takes the whole query as the address to open, and the settings validator
    /// has checked it (an https address on a sign-in host). The failure is a kind, or nothing when the tab is open.
    /// </summary>
    public async Task<string?> OpenAsync(string cdpUrl, string address, int timeoutSeconds, CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
            using var request = new HttpRequestMessage(HttpMethod.Put, new Uri(new Uri(cdpUrl), "/json/new?" + address));
            using var response = await _http.SendAsync(request, timeout.Token);
            response.EnsureSuccessStatusCode();
            return null;
        }
        catch (Exception ex) when (IsAFailureOfTheBrowser(ex, cancellationToken))
        {
            return ex.GetType().Name;
        }
    }

    private static bool IsAFailureOfTheBrowser(Exception ex, CancellationToken caller) =>
        ex is HttpRequestException or JsonException || (ex is OperationCanceledException && !caller.IsCancellationRequested);

    private sealed record TabInfo([property: JsonPropertyName("type")] string? Type, [property: JsonPropertyName("url")] string? Url);
}
