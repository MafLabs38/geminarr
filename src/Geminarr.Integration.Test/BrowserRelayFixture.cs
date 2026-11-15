using System.Net;
using System.Net.WebSockets;
using System.Text;
using Geminarr.Integration.Test.Support;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
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
