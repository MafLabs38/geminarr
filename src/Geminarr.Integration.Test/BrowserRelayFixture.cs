using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using Geminarr.Integration.Test.Support;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Geminarr.Integration.Test;

/// <summary>The relay of the web desktop of the browser (ADR 0017): closed without a sign-in, the only way to the desktop, and it keeps Geminarr's session to itself.</summary>
public sealed class BrowserRelayFixture
{
    private static readonly CancellationToken Token = TestContext.Current.CancellationToken;

    [Fact]
    public async Task should_send_a_person_without_a_session_to_the_sign_in_page_and_reach_the_desktop_for_nobody()
    {
        await using var desktop = await FakeDesktop.StartAsync();
        using var factory = new RelayFactory(desktop.Url);
        using var anonymous = factory.CreateClient(new() { AllowAutoRedirect = false });

        var page = await anonymous.GetAsync("/browser/", Token);
        var asset = await anonymous.GetAsync("/browser/assets/app.js", Token);

        page.StatusCode.Should().Be(HttpStatusCode.Redirect);
        page.Headers.Location!.OriginalString.Should().Be("/");
        asset.StatusCode.Should().Be(HttpStatusCode.Redirect);
        desktop.Seen.Should().BeEmpty("nothing of an anonymous request goes on");
    }

    [Fact]
    public async Task should_relay_the_desktop_to_the_operator_under_its_path_and_take_it_to_the_slash()
    {
        await using var desktop = await FakeDesktop.StartAsync();
        using var factory = new RelayFactory(desktop.Url);
        using var client = await factory.SignedInClientAsync();

        var page = await client.GetAsync("/browser/assets/app.js", Token);
        var bare = await factory.CreateClient(new() { AllowAutoRedirect = false }).GetAsync("/browser", Token);

        page.StatusCode.Should().Be(HttpStatusCode.OK);
        (await page.Content.ReadAsStringAsync(Token)).Should().Be("desktop:/browser/assets/app.js");
        bare.StatusCode.Should().Be(HttpStatusCode.Redirect);
    }

    [Fact]
    public async Task should_keep_the_session_of_geminarr_from_the_desktop_and_the_cookies_of_the_desktop_from_geminarr()
    {
        await using var desktop = await FakeDesktop.StartAsync();
        using var factory = new RelayFactory(desktop.Url);
        using var client = await factory.SignedInClientAsync();
        client.DefaultRequestHeaders.Add("Authorization", "Bearer something");

        var response = await client.GetAsync("/browser/", Token);

        desktop.Seen.Should().ContainSingle().Which.Should().Be(("/browser/", null, null), "neither the session nor the credentials go on");
        response.Headers.Contains("Set-Cookie").Should().BeFalse("the desktop sets no cookie on Geminarr's origin");
    }

    [Fact]
    public async Task should_give_the_pages_of_the_desktop_their_own_policy_and_every_other_page_the_strict_one()
    {
        await using var desktop = await FakeDesktop.StartAsync();
        using var factory = new RelayFactory(desktop.Url);
        using var client = await factory.SignedInClientAsync();

        var relayed = await client.GetAsync("/browser/", Token);
        var own = await client.GetAsync("/api/v1/connection", Token);

        relayed.Headers.GetValues("Content-Security-Policy").Single().Should().Contain("blob:").And.Contain("frame-ancestors 'none'");
        own.Headers.GetValues("Content-Security-Policy").Single().Should().Be("default-src 'self'; img-src 'self' data:; frame-ancestors 'none'; base-uri 'none'; form-action 'self'");
        relayed.Headers.GetValues("X-Frame-Options").Single().Should().Be("DENY");
    }

    [Fact]
    public async Task should_relay_a_websocket_of_the_same_origin_and_refuse_one_of_another()
    {
        await using var desktop = await FakeDesktop.StartAsync();
        // A WebSocket is upgraded by a real server: the test host cannot carry it through the relay.
        using var factory = new RelayFactory(desktop.Url);
        factory.UseKestrel(0);
        factory.StartServer();
        var address = new Uri(factory.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First());
        var cookie = await SessionCookieAsync(factory, address);
        var socketAddress = new Uri($"ws://{address.Authority}/browser/api/websockets");

        using var socket = await ConnectAsync(socketAddress, origin: address.GetLeftPart(UriPartial.Authority), cookie);
        await socket.SendAsync(Encoding.UTF8.GetBytes("hello"), WebSocketMessageType.Text, endOfMessage: true, Token);
        var buffer = new byte[64];
        var echo = await socket.ReceiveAsync(buffer, Token);

        Encoding.UTF8.GetString(buffer, 0, echo.Count).Should().Be("hello");
        await FluentActions.Awaiting(() => ConnectAsync(socketAddress, "https://evil.example", cookie)).Should().ThrowAsync<WebSocketException>("a page of another origin does not open a WebSocket to the desktop");
        await FluentActions.Awaiting(() => ConnectAsync(socketAddress, address.GetLeftPart(UriPartial.Authority), cookie: null)).Should().ThrowAsync<WebSocketException>("nor does anybody without a session");
        desktop.Seen.Should().ContainSingle("only the first one reached the desktop");
    }

    [Fact]
    public async Task should_end_a_websocket_with_the_session_that_opened_it()
    {
        await using var desktop = await FakeDesktop.StartAsync();
        using var factory = new RelayFactory(desktop.Url, movableClock: true);
        factory.UseKestrel(0);
        factory.StartServer();
        var address = new Uri(factory.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First());
        var cookie = await SessionCookieAsync(factory, address);
        using var socket = await ConnectAsync(new Uri($"ws://{address.Authority}/browser/api/websockets"), address.GetLeftPart(UriPartial.Authority), cookie);
        using var client = new HttpClient { BaseAddress = address };
        using var logout = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/logout");
        logout.Headers.Add("X-Geminarr-Request", "1");
        logout.Headers.Add("Cookie", cookie);

        (await client.SendAsync(logout, Token)).EnsureSuccessStatusCode();
        // The check of the session is a tick of the application's clock: the test moves it, and does not wait for it.
        using var patience = CancellationTokenSource.CreateLinkedTokenSource(Token);
        patience.CancelAfter(TimeSpan.FromSeconds(30));
        var ticking = Task.Run(async () =>
        {
            while (!patience.IsCancellationRequested)
            {
                factory.Clock!.Advance(TimeSpan.FromSeconds(6));
                await Task.Delay(50, CancellationToken.None);
            }
        }, CancellationToken.None);
        var ended = false;
        try
        {
            ended = (await socket.ReceiveAsync(new byte[64], patience.Token)).MessageType == WebSocketMessageType.Close;
        }
        catch (WebSocketException)
        {
            ended = true;
        }

        await patience.CancelAsync();
        await ticking;

        ended.Should().BeTrue("the desktop does not go on for somebody who signed out");
    }

    [Fact]
    public async Task should_refuse_a_write_that_a_page_of_another_origin_sends_and_pass_one_of_the_same_origin()
    {
        await using var desktop = await FakeDesktop.StartAsync();
        using var factory = new RelayFactory(desktop.Url);
        using var client = await factory.SignedInClientAsync();
        using var foreign = new HttpRequestMessage(HttpMethod.Post, "/browser/api/clipboard") { Content = new StringContent("x") };
        foreign.Headers.Add("Origin", "https://evil.example");
        using var own = new HttpRequestMessage(HttpMethod.Post, "/browser/api/clipboard") { Content = new StringContent("x") };
        own.Headers.Add("Origin", "http://localhost");
        using var read = new HttpRequestMessage(HttpMethod.Get, "/browser/") { };
        read.Headers.Add("Origin", "https://evil.example");

        (await client.SendAsync(foreign, Token)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.SendAsync(own, Token)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.SendAsync(read, Token)).StatusCode.Should().Be(HttpStatusCode.OK, "a read changes nothing, and the cookie is SameSite=Strict");
        desktop.Seen.Select(seen => seen.Path).Should().BeEquivalentTo(["/browser/api/clipboard", "/browser/"]);
    }

    [Fact]
    public async Task should_close_the_desktop_and_say_why_while_a_port_of_the_namespace_listens_off_the_loopback()
    {
        await using var desktop = await FakeDesktop.StartAsync();
        using var factory = new RelayFactory(desktop.Url);
        using var client = await factory.SignedInClientAsync();
        factory.Exposure.Ports.Add(3000);

        var closed = await client.GetAsync("/browser/", Token);
        var health = await client.GetFromJsonAsync<Geminarr.Api.V1.Health.HealthResource>("/api/v1/health", Token);

        closed.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await closed.Content.ReadAsStringAsync(Token)).Should().Contain("listens off the loopback: 3000");
        desktop.Seen.Should().BeEmpty("nothing is relayed to a desktop that others can reach");
        health!.Checks.Single(check => check.Name == "browser").Reason.Should().Be("browser-exposed");

        factory.Exposure.Ports.Clear();
        factory.Exposure.Unreadable = true;
        var unknown = await client.GetAsync("/browser/", Token);
        factory.Exposure.Unreadable = false;

        unknown.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable, "what cannot be checked is not taken for isolated");
        (await client.GetAsync("/browser/", Token)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task should_not_take_another_port_of_the_same_host_for_the_page()
    {
        await using var desktop = await FakeDesktop.StartAsync();
        using var factory = new RelayFactory(desktop.Url);
        using var client = await factory.SignedInClientAsync();
        HttpRequestMessage Write(string origin)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/browser/api/clipboard") { Content = new StringContent("x") };
            request.Headers.Host = "localhost:8282";
            request.Headers.Add("Origin", origin);
            return request;
        }

        (await client.SendAsync(Write("http://localhost:8282"), Token)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.SendAsync(Write("https://localhost:8282"), Token)).StatusCode.Should().Be(HttpStatusCode.OK, "the scheme cannot be told behind a proxy that ends TLS");
        (await client.SendAsync(Write("http://localhost:9000"), Token)).StatusCode.Should().Be(HttpStatusCode.Forbidden, "a hostile page on another port of the same host is another origin");
        (await client.SendAsync(Write("http://localhost"), Token)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task should_judge_an_origin_by_the_names_the_page_may_be_reached_by_and_not_by_the_host_a_proxy_sends()
    {
        await using var desktop = await FakeDesktop.StartAsync();
        using var factory = new RelayFactory(desktop.Url, allowedHostNames: "localhost;geminarr.internal");
        using var client = await factory.SignedInClientAsync();
        HttpRequestMessage Write(string origin)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/browser/api/clipboard") { Content = new StringContent("x") };
            request.Headers.Host = "geminarr.internal:8282";
            request.Headers.Add("Origin", origin);
            return request;
        }

        (await client.SendAsync(Write("https://localhost"), Token)).StatusCode.Should().Be(HttpStatusCode.OK, "a proxy that sends its own Host does not make the page foreign to itself");
        (await client.SendAsync(Write("https://evil.example"), Token)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.SendAsync(Write("http://localhost:9000"), Token)).StatusCode.Should().Be(HttpStatusCode.Forbidden, "another site on the same host is not the page, even if the host is a name the page has");
    }

    [Fact]
    public async Task should_speak_to_the_desktop_with_the_one_origin_that_it_allows_whatever_the_page_of_the_person_is()
    {
        await using var desktop = await FakeDesktop.StartAsync();
        using var factory = new RelayFactory(desktop.Url);
        using var client = await factory.SignedInClientAsync();
        using var withOrigin = new HttpRequestMessage(HttpMethod.Post, "/browser/api/clipboard") { Content = new StringContent("x") };
        withOrigin.Headers.Add("Origin", "http://localhost");

        (await client.SendAsync(withOrigin, Token)).EnsureSuccessStatusCode();
        (await client.GetAsync("/browser/", Token)).EnsureSuccessStatusCode();

        desktop.Origins.Should().OnlyContain(origin => origin == "https://geminarr.invalid", "the desktop allows this origin, which no web page can have");
    }

    [Fact]
    public async Task should_say_bad_gateway_when_the_desktop_is_not_there_yet()
    {
        await using var desktop = await FakeDesktop.StartAsync();
        var gone = desktop.Url;
        await desktop.DisposeAsync();
        using var factory = new RelayFactory(gone);
        using var client = await factory.SignedInClientAsync();

        var response = await client.GetAsync("/browser/", Token);

        response.StatusCode.Should().Be(HttpStatusCode.BadGateway, "the browser container may be starting: the page says so, and the operator's pages are unaffected");
    }

    [Theory]
    [InlineData("/browser", true)]
    [InlineData("/browser/", true)]
    [InlineData("/browser/assets/app.js", true)]
    [InlineData("/BROWSER/x", true)]
    [InlineData("/browserx", false)]
    [InlineData("/browser-x/", false)]
    [InlineData("/api/v1/browser", false)]
    [InlineData("/", false)]
    public void should_give_the_loose_policy_to_the_path_of_the_relay_and_to_no_other(string path, bool relay)
    {
        Geminarr.Host.Http.BrowserRelay.IsRelayPath(new PathString(path)).Should().Be(relay);
    }

    private static async Task<ClientWebSocket> ConnectAsync(Uri address, string origin, string? cookie)
    {
        var socket = new ClientWebSocket();
        socket.Options.SetRequestHeader("Origin", origin);
        if (cookie is not null)
        {
            socket.Options.SetRequestHeader("Cookie", cookie);
        }

        await socket.ConnectAsync(address, Token);
        return socket;
    }

    /// <summary>The cookie of a session, as a browser would send it back (the client of the factory keeps it in a jar that a test cannot read).</summary>
    private static async Task<string> SessionCookieAsync(RelayFactory factory, Uri address)
    {
        using var client = new HttpClient { BaseAddress = address };
        using var login = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login") { Content = System.Net.Http.Json.JsonContent.Create(new { name = GeminarrFactoryBase.OperatorName, password = GeminarrFactoryBase.OperatorPassword }) };
        login.Headers.Add("X-Geminarr-Request", "1");
        using var response = await client.SendAsync(login, Token);
        return response.Headers.GetValues("Set-Cookie").Single().Split(';')[0];
    }
}
