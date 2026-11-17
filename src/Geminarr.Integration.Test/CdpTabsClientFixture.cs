using System.Net;
using Geminarr.Browser;

namespace Geminarr.Integration.Test;

/// <summary>The plain-HTTP half of the browser's debugging address: the list of the tabs, and the tab that a person signs in on. Tested with a handler in place of the browser, so with no browser.</summary>
public sealed class CdpTabsClientFixture
{
    private const string Cdp = "http://127.0.0.1:9222";
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public List<(HttpMethod Method, string PathAndQuery)> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request.Method, request.RequestUri!.PathAndQuery));
            return respond(request, cancellationToken);
        }
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

    [Fact]
    public async Task should_list_the_tabs_with_their_type_and_address()
    {
        var handler = new Handler((_, _) => Task.FromResult(Json("""[{"type":"page","url":"https://accounts.example/signin"},{"type":"service_worker","url":null}]""")));
        var subject = new CdpTabsClient(new HttpClient(handler));

        var result = await subject.ListAsync(Cdp, 5, Token);

        result.Failure.Should().BeNull();
        result.Tabs!.Select(tab => (tab.Type, tab.Url)).Should().Equal(("page", "https://accounts.example/signin"), ("service_worker", null));
        handler.Requests.Should().Equal((HttpMethod.Get, "/json/list"));
    }

    [Fact]
    public async Task should_list_no_tab_for_an_empty_answer()
    {
        var subject = new CdpTabsClient(new HttpClient(new Handler((_, _) => Task.FromResult(Json("null")))));

        var result = await subject.ListAsync(Cdp, 5, Token);

        result.Tabs.Should().BeEmpty();
        result.Failure.Should().BeNull();
    }

    [Theory]
    [InlineData("not json", HttpStatusCode.OK, "JsonException")]
    [InlineData("[]", HttpStatusCode.InternalServerError, "HttpRequestException")]
    public async Task should_say_the_kind_of_the_failure_and_nothing_of_what_the_browser_said(string body, HttpStatusCode status, string kind)
    {
        var subject = new CdpTabsClient(new HttpClient(new Handler((_, _) => Task.FromResult(Json(body, status)))));

        var result = await subject.ListAsync(Cdp, 5, Token);

        result.Tabs.Should().BeNull();
        result.Failure.Should().Be(kind);
    }

    [Fact]
    public async Task should_say_the_browser_failed_when_it_cannot_be_reached()
    {
        var subject = new CdpTabsClient(new HttpClient(new Handler((_, _) => throw new HttpRequestException("connection refused at 127.0.0.1:9222"))));

        var result = await subject.ListAsync(Cdp, 5, Token);

        result.Failure.Should().Be(nameof(HttpRequestException));
    }

    [Fact]
    public async Task should_give_up_on_a_browser_that_does_not_answer_in_time_and_not_on_the_callers_cancellation()
    {
        var hangs = new Handler(async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return Json("[]");
        });
        var subject = new CdpTabsClient(new HttpClient(hangs));

        var late = await subject.ListAsync(Cdp, 1, Token);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var cancelling = () => subject.ListAsync(Cdp, 30, cancelled.Token);

        late.Failure.Should().NotBeNull("a browser that is silent is a browser that failed");
        await cancelling.Should().ThrowAsync<OperationCanceledException>("the pass that gave up is not a failure of the browser");
    }

    [Fact]
    public async Task should_open_a_tab_with_the_address_as_the_query_without_escaping_it()
    {
        var handler = new Handler((_, _) => Task.FromResult(Json("{}")));
        var subject = new CdpTabsClient(new HttpClient(handler));

        var failure = await subject.OpenAsync(Cdp, "https://accounts.example/ServiceLogin?continue=https%3A%2F%2Fassistant.example%2Fapp", 5, Token);

        failure.Should().BeNull();
        handler.Requests.Should().Equal((HttpMethod.Put, "/json/new?https://accounts.example/ServiceLogin?continue=https%3A%2F%2Fassistant.example%2Fapp"));
    }

    [Fact]
    public async Task should_say_the_kind_of_the_failure_when_the_tab_cannot_be_opened()
    {
        var refused = new CdpTabsClient(new HttpClient(new Handler((_, _) => Task.FromResult(Json("{}", HttpStatusCode.InternalServerError)))));
        var gone = new CdpTabsClient(new HttpClient(new Handler((_, _) => throw new HttpRequestException("down"))));

        (await refused.OpenAsync(Cdp, "https://accounts.example/", 5, Token)).Should().Be(nameof(HttpRequestException));
        (await gone.OpenAsync(Cdp, "https://accounts.example/", 5, Token)).Should().Be(nameof(HttpRequestException));
    }

    [Fact]
    public async Task should_give_up_on_a_tab_that_takes_too_long_to_open()
    {
        var subject = new CdpTabsClient(new HttpClient(new Handler(async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return Json("{}");
        })));

        (await subject.OpenAsync(Cdp, "https://accounts.example/", 1, Token)).Should().NotBeNull();
    }

    [Fact]
    public async Task should_not_let_an_address_with_line_breaks_split_the_request()
    {
        var handler = new Handler((_, _) => Task.FromResult(Json("{}")));
        var subject = new CdpTabsClient(new HttpClient(handler));

        await subject.OpenAsync(Cdp, "https://accounts.example/x\r\nHost: evil.example", 5, Token);

        handler.Requests.Should().ContainSingle().Which.PathAndQuery.Should().NotContainAny("\r", "\n");
    }

    [Fact]
    public async Task should_not_read_a_list_of_tabs_that_weighs_more_than_the_limit()
    {
        var huge = "[{\"type\":\"page\",\"url\":\"https://example.org/" + new string('a', CdpTabsClient.MaxListBytes) + "\"}]";
        var subject = new CdpTabsClient(new HttpClient(new Handler((_, _) => Task.FromResult(Json(huge)))));

        var result = await subject.ListAsync(Cdp, 5, Token);

        result.Tabs.Should().BeNull();
        result.Failure.Should().Be("TooLarge", "a page can make its address as long as it likes: the list is read up to a limit and no more");
    }

    [Fact]
    public async Task should_read_a_list_of_tabs_that_weighs_exactly_the_limit()
    {
        const string head = "[{\"type\":\"page\",\"url\":\"https://example.org/";
        const string tail = "\"}]";
        var body = head + new string('a', CdpTabsClient.MaxListBytes - head.Length - tail.Length) + tail;
        body.Length.Should().Be(CdpTabsClient.MaxListBytes);
        var subject = new CdpTabsClient(new HttpClient(new Handler((_, _) => Task.FromResult(Json(body)))));

        var result = await subject.ListAsync(Cdp, 5, Token);

        result.Failure.Should().BeNull("the limit is on what is more than a megabyte, not on a megabyte");
        result.Tabs.Should().ContainSingle();
    }
}
