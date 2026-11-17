using System.Net;
using System.Net.Http.Json;
using Geminarr.Api.V1.Connection;
using Microsoft.Extensions.DependencyInjection;

namespace Geminarr.Integration.Test;

/// <summary>The connection to the assistant through the API, with a browser in memory: the rules of access, the state, the page of the browser, the pause and the disconnect.</summary>
public sealed class ConnectionApiFixture
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("POST", "/api/v1/connection/pause")]
    [InlineData("POST", "/api/v1/connection/sign-in")]
    [InlineData("POST", "/api/v1/connection/resume")]
    [InlineData("POST", "/api/v1/connection/disconnect")]
    public async Task should_refuse_a_write_without_a_sign_in_and_without_the_header(string method, string path)
    {
        using var factory = new BrowserFactory();
        using var anonymous = factory.CreateClient();
        anonymous.DefaultRequestHeaders.Add("X-Geminarr-Request", "1");
        using var withoutHeader = await factory.SignedInClientAsync(sendsTheHeader: false);

        (await anonymous.SendAsync(new HttpRequestMessage(new HttpMethod(method), path), Token)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await withoutHeader.SendAsync(new HttpRequestMessage(new HttpMethod(method), path), Token)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task should_refuse_to_read_the_state_without_a_sign_in()
    {
        using var factory = new BrowserFactory();
        using var anonymous = factory.CreateClient();

        (await anonymous.GetAsync("/api/v1/connection", Token)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task should_say_there_is_no_browser_to_pause_when_no_browser_reads()
    {
        using var factory = new GeminarrFactory();
        using var client = await factory.SignedInClientAsync();

        (await client.PostAsJsonAsync("/api/v1/connection/pause", new { hours = 1 }, Token)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.GetFromJsonAsync<ConnectionResource>("/api/v1/connection", Token))!.HasBrowser.Should().BeFalse();
    }

    [Fact]
    public async Task should_say_that_a_desktop_of_the_browser_is_there_to_sign_in_to()
    {
        using var factory = new BrowserFactory();
        using var client = await factory.SignedInClientAsync();

        var connection = await client.GetFromJsonAsync<ConnectionResource>("/api/v1/connection", Token);

        connection!.HasBrowser.Should().BeTrue();
        connection.HasDesktop.Should().BeTrue();
    }

    [Fact]
    public async Task should_give_the_address_of_the_desktops_own_origin_with_a_ticket_that_is_worth_one_opening()
    {
        using var factory = new BrowserFactory();
        using var client = await factory.SignedInClientAsync();

        var response = await client.PostAsync("/api/v1/connection/desktop", content: null, Token);
        var url = new Uri((await response.Content.ReadFromJsonAsync<DesktopResource>(Token))!.Url);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        url.Scheme.Should().Be("http");
        url.Host.Should().Be("localhost", "the desktop is the page's host on another port");
        url.Port.Should().Be(8283);
        url.AbsolutePath.Should().Be("/enter");
        System.Web.HttpUtility.ParseQueryString(url.Query)["ticket"].Should().NotBeNullOrEmpty();
        response.Headers.CacheControl!.NoStore.Should().BeTrue("a ticket is not kept by any cache");
    }

    [Fact]
    public async Task should_give_the_address_that_a_proxy_gives_the_desktop_when_one_is_set()
    {
        using var factory = new RelayFactory("http://127.0.0.1:3000", desktopPublicUrl: "https://desktop.homelab.example");
        using var client = await factory.SignedInClientAsync();

        var url = new Uri((await (await client.PostAsync("/api/v1/connection/desktop", content: null, Token)).Content.ReadFromJsonAsync<DesktopResource>(Token))!.Url);

        url.GetLeftPart(UriPartial.Authority).Should().Be("https://desktop.homelab.example");
    }

    [Fact]
    public async Task should_not_give_a_desktop_when_none_is_configured_and_not_to_a_person_who_is_not_signed_in()
    {
        using var factory = new GeminarrFactory();
        using var client = await factory.SignedInClientAsync();

        (await client.PostAsync("/api/v1/connection/desktop", content: null, Token)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var withBrowser = new BrowserFactory();
        using var anonymousOnBrowser = withBrowser.CreateClient();
        anonymousOnBrowser.DefaultRequestHeaders.Add("X-Geminarr-Request", "1");
        (await anonymousOnBrowser.PostAsync("/api/v1/connection/desktop", content: null, Token)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task should_say_who_the_browser_is_signed_in_as_to_the_operator_and_nobody_when_no_check_found_out()
    {
        using var factory = new BrowserFactory();
        using var client = await factory.SignedInClientAsync();
        (await client.GetFromJsonAsync<ConnectionResource>("/api/v1/connection", Token))!.Account.Should().BeNull();

        factory.Browser.Account = new Geminarr.Core.Browser.AccountIdentity("Jane Doe", "jane@example.org");
        (await client.GetFromJsonAsync<ConnectionResource>("/api/v1/connection", Token))!.Account.Should().BeNull("nobody is connected yet");

        factory.Services.GetRequiredService<Geminarr.Core.Connection.ConnectionTracker>().Connected();

        (await client.GetFromJsonAsync<ConnectionResource>("/api/v1/connection", Token))!.Account.Should().Be(new AccountResource("Jane Doe", "jane@example.org"));
    }

    [Fact]
    public async Task should_open_the_sign_in_page_of_the_browser_when_asked_and_say_so_when_the_browser_is_gone()
    {
        using var factory = new BrowserFactory();
        using var client = await factory.SignedInClientAsync();

        (await client.PostAsync("/api/v1/connection/sign-in", content: null, Token)).EnsureSuccessStatusCode();
        factory.Browser.SignInPagesOpened.Should().Be(1);

        factory.Browser.Unreachable = true;
        var gone = await client.PostAsync("/api/v1/connection/sign-in", content: null, Token);

        gone.StatusCode.Should().Be(HttpStatusCode.BadGateway);
        (await gone.Content.ReadAsStringAsync(Token)).Should().NotContain("127.0.0.1").And.NotContain("9222");
    }

    [Fact]
    public async Task should_refuse_to_open_a_sign_in_page_when_no_browser_reads()
    {
        using var factory = new GeminarrFactory();
        using var client = await factory.SignedInClientAsync();

        (await client.PostAsync("/api/v1/connection/sign-in", content: null, Token)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task should_never_give_the_debugging_address_or_the_names_of_the_cookies()
    {
        using var factory = new BrowserFactory();
        using var client = await factory.SignedInClientAsync();

        var body = await client.GetStringAsync("/api/v1/connection", Token);

        body.Should().NotContain("127.0.0.1").And.NotContain("9222").And.NotContain("__Secure-1PSID").And.NotContain("SessionCookies");
    }

    [Theory]
    [InlineData(720, true)]
    [InlineData(720.01, false)]
    [InlineData(-1, false)]
    public async Task should_accept_a_pause_up_to_thirty_days_and_refuse_a_longer_one_or_a_negative_one(double hours, bool accepted)
    {
        using var factory = new BrowserFactory();
        using var client = await factory.SignedInClientAsync();

        var response = await client.PostAsJsonAsync("/api/v1/connection/pause", new { hours }, Token);

        response.IsSuccessStatusCode.Should().Be(accepted);
    }

    [Fact]
    public async Task should_pause_with_a_limit_resume_and_disconnect()
    {
        using var factory = new BrowserFactory();
        using var client = await factory.SignedInClientAsync();

        (await client.PostAsJsonAsync("/api/v1/connection/pause", new { hours = 0 }, Token)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.PostAsJsonAsync("/api/v1/connection/pause", new { hours = 24 * 31 }, Token)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var paused = await (await client.PostAsJsonAsync("/api/v1/connection/pause", new { hours = 6 }, Token)).Content.ReadFromJsonAsync<ConnectionResource>(Token);
        paused!.State.Should().Be("Paused");
        paused.PausedUntil.Should().BeCloseTo(DateTimeOffset.UtcNow.AddHours(6), TimeSpan.FromMinutes(1));

        var resumed = await (await client.PostAsync("/api/v1/connection/resume", content: null, Token)).Content.ReadFromJsonAsync<ConnectionResource>(Token);
        resumed!.State.Should().Be("NotConnected");

        var untilResumed = await (await client.PostAsJsonAsync("/api/v1/connection/pause", new { hours = (double?)null }, Token)).Content.ReadFromJsonAsync<ConnectionResource>(Token);
        untilResumed!.PausedUntil.Should().BeNull();
        var disconnected = await (await client.PostAsync("/api/v1/connection/disconnect", content: null, Token)).Content.ReadFromJsonAsync<ConnectionResource>(Token);
        disconnected!.State.Should().Be("NotConnected");
        factory.Browser.Disconnected.Should().Be(1);
    }

    [Fact]
    public async Task should_tell_the_operator_that_the_browser_could_not_be_reached_and_forget_the_connection_all_the_same()
    {
        using var factory = new BrowserFactory();
        using var client = await factory.SignedInClientAsync();
        factory.Browser.Unreachable = true;

        var response = await client.PostAsync("/api/v1/connection/disconnect", content: null, Token);

        response.StatusCode.Should().Be(HttpStatusCode.BadGateway);
        (await response.Content.ReadAsStringAsync(Token)).Should().Contain("could not be reached");
        (await client.GetFromJsonAsync<ConnectionResource>("/api/v1/connection", Token))!.State.Should().Be("NotConnected");
    }
}
