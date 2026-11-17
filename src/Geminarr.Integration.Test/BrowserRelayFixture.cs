using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using Geminarr.Core.Access;
using Geminarr.Integration.Test.Support;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Geminarr.Integration.Test;

/// <summary>The relay of the web desktop of the browser, on an origin of its own (ADR 0017, 0019): closed without the cookie that a ticket of the operator's session gives, the only way to the desktop, and it keeps Geminarr's session to itself.</summary>
public sealed class BrowserRelayFixture
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task should_reach_the_desktop_for_nobody_without_the_cookie_that_a_ticket_gives()
    {
        await using var desktop = await FakeDesktop.StartAsync();
        using var factory = new RelayFactory(desktop.Url);
        using var anonymous = factory.Anonymous();

        var page = await anonymous.GetAsync("/browser/", Token);
        var asset = await anonymous.GetAsync("/browser/assets/app.js", Token);
        var forged = await anonymous.GetAsync("/enter?ticket=not-a-ticket", Token);
        var empty = await anonymous.GetAsync("/enter", Token);

        new[] { page, asset, forged, empty }.Should().OnlyContain(response => response.StatusCode == HttpStatusCode.Unauthorized);
        desktop.Seen.Should().BeEmpty("nothing of an anonymous request goes on");
    }

    [Fact]
    public async Task should_not_serve_the_desktop_on_the_origin_of_the_interface_nor_the_interface_on_the_desktops()
    {
        await using var desktop = await FakeDesktop.StartAsync();
        using var factory = new RelayFactory(desktop.Url);
        using var operatorClient = await factory.SignedInClientAsync();
        using var desktopClient = await factory.DesktopClientAsync(operatorClient);

        var onTheInterface = await operatorClient.GetAsync("/browser/assets/app.js", Token);
        var api = await desktopClient.GetAsync("/api/v1/connection", Token);
        var page = await desktopClient.GetAsync("/", Token);
        var login = await desktopClient.PostAsJsonAsync("/api/v1/auth/login", new { name = GeminarrFactoryBase.OperatorName, password = GeminarrFactoryBase.OperatorPassword }, Token);

        (await onTheInterface.Content.ReadAsStringAsync(Token)).Should().NotContain("desktop:", "the desktop's pages never run on the origin where the operator's cookie is sent");
        new[] { api, page, login }.Should().OnlyContain(response => response.StatusCode == HttpStatusCode.NotFound, "the desktop's origin answers for the desktop and nothing else");
        desktop.Seen.Should().BeEmpty();
    }

    [Fact]
    public async Task should_relay_the_desktop_under_its_path_and_take_it_to_the_slash()
    {
        await using var desktop = await FakeDesktop.StartAsync();
        using var factory = new RelayFactory(desktop.Url);
        using var client = await factory.DesktopClientAsync();

        var page = await client.GetAsync("/browser/assets/app.js", Token);
        var bare = await client.GetAsync("/browser", Token);

        page.StatusCode.Should().Be(HttpStatusCode.OK);
        (await page.Content.ReadAsStringAsync(Token)).Should().Be("desktop:/browser/assets/app.js");
        bare.StatusCode.Should().Be(HttpStatusCode.Redirect);
        bare.Headers.Location!.OriginalString.Should().Be("/browser/");
    }

    [Fact]
    public async Task should_exchange_a_ticket_once_for_a_cookie_of_the_desktops_origin_and_leave_it_out_of_the_address_that_follows()
    {
        await using var desktop = await FakeDesktop.StartAsync();
        using var factory = new RelayFactory(desktop.Url);
        using var operatorClient = await factory.SignedInClientAsync();
        var ticket = await RelayFactory.TicketAsync(operatorClient);
        using var first = factory.Anonymous();
        using var second = factory.Anonymous();

        var entered = await first.GetAsync($"/enter?ticket={ticket}", Token);
        var again = await second.GetAsync($"/enter?ticket={ticket}", Token);

        entered.StatusCode.Should().Be(HttpStatusCode.Redirect);
        entered.Headers.Location!.OriginalString.Should().Be("/browser/", "the ticket does not stay in the address bar");
        var cookie = entered.Headers.GetValues("Set-Cookie").Single();
        cookie.Should().Contain("httponly").And.Contain("samesite=strict").And.Contain("path=/");
        cookie.Should().NotContain(ticket, "the cookie is a secret of its own");
        again.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "a ticket is worth one opening");
        (await first.GetAsync("/browser/", Token)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task should_not_exchange_a_ticket_that_waited_too_long_or_whose_session_ended()
    {
        await using var desktop = await FakeDesktop.StartAsync();
        using var factory = new RelayFactory(desktop.Url, movableClock: true);
        using var operatorClient = await factory.SignedInClientAsync();
        var late = await RelayFactory.TicketAsync(operatorClient);
        var orphan = await RelayFactory.TicketAsync(operatorClient);
        factory.Clock!.Advance(DesktopAccess.TicketLifetime + TimeSpan.FromSeconds(1));
        var fresh = await RelayFactory.TicketAsync(operatorClient);

        using var logout = await operatorClient.PostAsync("/api/v1/auth/logout", content: null, Token);

        using var anonymous = factory.Anonymous();
        (await anonymous.GetAsync($"/enter?ticket={late}", Token)).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "a minute is all a ticket has");
        (await anonymous.GetAsync($"/enter?ticket={orphan}", Token)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anonymous.GetAsync($"/enter?ticket={fresh}", Token)).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "the session that asked for it is gone");
    }

    [Fact]
    public async Task should_end_the_desktops_cookie_with_the_session_behind_it_and_not_take_it_for_the_operators()
    {
        await using var desktop = await FakeDesktop.StartAsync();
        using var factory = new RelayFactory(desktop.Url);
        using var operatorClient = await factory.SignedInClientAsync();
        using var desktopClient = await factory.DesktopClientAsync(operatorClient);
        (await desktopClient.GetAsync("/browser/", Token)).StatusCode.Should().Be(HttpStatusCode.OK);
        var desktopCookie = await DesktopCookieAsync(factory, operatorClient);
        using var withTheDesktopsCookie = factory.CreateClient(new() { AllowAutoRedirect = false, HandleCookies = false });
        using var asTheOperator = new HttpRequestMessage(HttpMethod.Get, "/api/v1/connection");
        asTheOperator.Headers.Add("Cookie", desktopCookie);

        (await withTheDesktopsCookie.SendAsync(asTheOperator, Token)).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "the desktop's cookie is worth nothing on the interface");

        using var logout = await operatorClient.PostAsync("/api/v1/auth/logout", content: null, Token);
        (await desktopClient.GetAsync("/browser/", Token)).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "signing out of Geminarr ends the desktop too");
    }

    [Fact]
    public async Task should_keep_the_session_of_geminarr_from_the_desktop_and_the_cookies_of_the_desktop_from_geminarr()
    {
        await using var desktop = await FakeDesktop.StartAsync();
        using var factory = new RelayFactory(desktop.Url);
        using var client = await factory.DesktopClientAsync();
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
        using var operatorClient = await factory.SignedInClientAsync();
        using var client = await factory.DesktopClientAsync(operatorClient);

        var relayed = await client.GetAsync("/browser/", Token);
        var own = await operatorClient.GetAsync("/api/v1/connection", Token);
        var refused = await client.GetAsync("/enter?ticket=not-a-ticket", Token);

        relayed.Headers.GetValues("Content-Security-Policy").Single().Should().Contain("blob:").And.Contain("frame-ancestors 'none'");
        own.Headers.GetValues("Content-Security-Policy").Single().Should().Be("default-src 'self'; img-src 'self' data:; frame-ancestors 'none'; base-uri 'none'; form-action 'self'");
        refused.Headers.GetValues("Content-Security-Policy").Single().Should().StartWith("default-src 'self'; img-src", "only the desktop's pages get the loose policy, not the page that takes a ticket");
        relayed.Headers.GetValues("X-Frame-Options").Single().Should().Be("DENY");
    }

    [Theory]
    [InlineData(false, "geminarr.desktop=", false)]
    [InlineData(true, "__Host-geminarr.desktop=", true)]
    public async Task should_set_the_desktops_cookie_as_secure_as_the_operators_and_with_the_prefix_that_needs_it(bool secure, string name, bool hasSecure)
    {
        await using var desktop = await FakeDesktop.StartAsync();
        using var factory = new RelayFactory(desktop.Url, secureCookie: secure);
        var scheme = secure ? "https" : "http";
        using var operatorClient = factory.CreateClient(new() { BaseAddress = new Uri($"{scheme}://localhost") });
        using var login = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login") { Content = JsonContent.Create(new { name = GeminarrFactoryBase.OperatorName, password = GeminarrFactoryBase.OperatorPassword }) };
        login.Headers.Add("X-Geminarr-Request", "1");
        (await operatorClient.SendAsync(login, Token)).EnsureSuccessStatusCode();
        operatorClient.DefaultRequestHeaders.Add("X-Geminarr-Request", "1");
        var ticket = await RelayFactory.TicketAsync(operatorClient);
        using var anonymous = factory.CreateClient(new() { AllowAutoRedirect = false, BaseAddress = new Uri($"{scheme}://localhost") });
        anonymous.DefaultRequestHeaders.Add(RelayFactory.LocalPortHeader, factory.DesktopPort.ToString(System.Globalization.CultureInfo.InvariantCulture));

        var entered = await anonymous.GetAsync($"/enter?ticket={ticket}", Token);

        var cookie = entered.Headers.GetValues("Set-Cookie").Single();
        cookie.Should().StartWith(name);
        cookie.Contains("secure", StringComparison.OrdinalIgnoreCase).Should().Be(hasSecure);
        cookie.Should().Contain("path=/").And.NotContain("domain=", "a __Host- cookie has no Domain");
    }

    [Fact]
    public async Task should_not_exchange_a_ticket_by_any_method_but_get()
    {
        await using var desktop = await FakeDesktop.StartAsync();
        using var factory = new RelayFactory(desktop.Url);
        using var operatorClient = await factory.SignedInClientAsync();
        var ticket = await RelayFactory.TicketAsync(operatorClient);
        using var anonymous = factory.Anonymous();
        anonymous.DefaultRequestHeaders.Add("X-Geminarr-Request", "1");

        var posted = await anonymous.PostAsync($"/enter?ticket={ticket}", content: null, Token);

        posted.StatusCode.Should().Be(HttpStatusCode.NotFound);
        posted.Headers.Contains("Set-Cookie").Should().BeFalse();
        (await anonymous.GetAsync($"/enter?ticket={ticket}", Token)).StatusCode.Should().Be(HttpStatusCode.Redirect, "the ticket was not used up by the other method");
    }

    [Fact]
    public async Task should_keep_the_apis_writes_and_its_answers_closed_to_a_page_of_the_desktops_origin()
    {
        // The operator's cookie goes to every port of the same host: what keeps the desktop's pages out of the API is that no write goes without its header and that no answer is ever readable by another origin.
        await using var desktop = await FakeDesktop.StartAsync();
        using var factory = new RelayFactory(desktop.Url);
        using var operatorClient = await factory.SignedInClientAsync(sendsTheHeader: false);
        var desktopOrigin = $"http://localhost:{factory.DesktopPort}";
        using var write = new HttpRequestMessage(HttpMethod.Post, "/api/v1/connection/pause") { Content = JsonContent.Create(new { hours = 1 }) };
        write.Headers.Add("Origin", desktopOrigin);
        using var preflight = new HttpRequestMessage(HttpMethod.Options, "/api/v1/connection/pause");
        preflight.Headers.Add("Origin", desktopOrigin);
        preflight.Headers.Add("Access-Control-Request-Method", "POST");
        preflight.Headers.Add("Access-Control-Request-Headers", "x-geminarr-request");
        using var read = new HttpRequestMessage(HttpMethod.Get, "/api/v1/connection");
        read.Headers.Add("Origin", desktopOrigin);

        var refused = await operatorClient.SendAsync(write, Token);
        var preflighted = await operatorClient.SendAsync(preflight, Token);
        var answered = await operatorClient.SendAsync(read, Token);

        refused.StatusCode.Should().Be(HttpStatusCode.Forbidden, "a write needs the header, which another origin cannot send without a preflight");
        new[] { refused, preflighted, answered }.SelectMany(response => response.Headers.Select(header => header.Key)).Should().NotContain(name => name.StartsWith("Access-Control-", StringComparison.OrdinalIgnoreCase), "no answer is ever readable by another origin");
    }

    [Fact]
    public async Task should_answer_to_the_host_that_a_proxy_gives_the_desktop_on_the_desktops_origin_and_not_on_the_pages()
    {
        await using var desktop = await FakeDesktop.StartAsync();
        using var factory = new RelayFactory(desktop.Url, desktopPublicUrl: "https://desktop.homelab.example");
        using var operatorClient = await factory.SignedInClientAsync();
        using var desktopClient = await factory.DesktopClientAsync(operatorClient);
        using var onTheDesktop = new HttpRequestMessage(HttpMethod.Get, "/browser/");
        onTheDesktop.Headers.Host = "desktop.homelab.example";
        using var onThePage = new HttpRequestMessage(HttpMethod.Get, "/api/v1/connection");
        onThePage.Headers.Host = "desktop.homelab.example";

        (await desktopClient.SendAsync(onTheDesktop, Token)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await operatorClient.SendAsync(onThePage, Token)).StatusCode.Should().Be(HttpStatusCode.BadRequest, "the page's origin answers to the page's names");
    }

    [Fact]
    public async Task should_relay_a_websocket_of_the_same_origin_and_refuse_one_of_another()
    {
        await using var desktop = await FakeDesktop.StartAsync();
        // A WebSocket is upgraded by a real server: the test host cannot carry it through the relay.
        using var factory = new RelayFactory(desktop.Url);
        var (address, cookies) = await OpenRealAsync(factory);
        var socketAddress = new Uri($"ws://{address.Authority}/browser/api/websockets");

        using var socket = await ConnectAsync(socketAddress, origin: address.GetLeftPart(UriPartial.Authority), cookies.Desktop);
        await socket.SendAsync(Encoding.UTF8.GetBytes("hello"), WebSocketMessageType.Text, endOfMessage: true, Token);
        var buffer = new byte[64];
        var echo = await socket.ReceiveAsync(buffer, Token);

        Encoding.UTF8.GetString(buffer, 0, echo.Count).Should().Be("hello");
        await FluentActions.Awaiting(() => ConnectAsync(socketAddress, "https://evil.example", cookies.Desktop)).Should().ThrowAsync<WebSocketException>("a page of another origin does not open a WebSocket to the desktop");
        await FluentActions.Awaiting(() => ConnectAsync(socketAddress, address.GetLeftPart(UriPartial.Authority), cookie: null)).Should().ThrowAsync<WebSocketException>("nor does anybody without the cookie of the desktop");
        await FluentActions.Awaiting(() => ConnectAsync(socketAddress, address.GetLeftPart(UriPartial.Authority), cookies.Operator)).Should().ThrowAsync<WebSocketException>("nor does the operator's own cookie open it");
        desktop.Seen.Should().ContainSingle("only the first one reached the desktop");
    }

    [Fact]
    public async Task should_end_a_websocket_with_the_session_that_opened_it()
    {
        await using var desktop = await FakeDesktop.StartAsync();
        using var factory = new RelayFactory(desktop.Url, movableClock: true);
        var (address, cookies) = await OpenRealAsync(factory);
        using var socket = await ConnectAsync(new Uri($"ws://{address.Authority}/browser/api/websockets"), address.GetLeftPart(UriPartial.Authority), cookies.Desktop);
        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{cookies.MainPort}") };
        using var logout = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/logout");
        logout.Headers.Add("X-Geminarr-Request", "1");
        logout.Headers.Add("Cookie", cookies.Operator);

        // It is open and relayed before the sign-out: what is sent comes back.
        await socket.SendAsync(Encoding.UTF8.GetBytes("hello"), WebSocketMessageType.Text, endOfMessage: true, Token);
        var echo = await socket.ReceiveAsync(new byte[64], Token);
        echo.MessageType.Should().Be(WebSocketMessageType.Text);
        desktop.Seen.Should().ContainSingle("the desktop has the one socket");

        (await client.SendAsync(logout, Token)).EnsureSuccessStatusCode();
        // The check of the session is a tick of the application's clock (the session lasts hours): the test gives it a few seconds of that clock at a time, until the socket has ended, and sleeps for no longer.
        using var patience = CancellationTokenSource.CreateLinkedTokenSource(Token);
        patience.CancelAfter(TimeSpan.FromSeconds(30));
        var ended = false;
        var receiving = socket.ReceiveAsync(new byte[64], patience.Token);
        while (!receiving.IsCompleted && !patience.IsCancellationRequested)
        {
            factory.Clock!.Advance(TimeSpan.FromSeconds(6));
            await Task.Delay(50, CancellationToken.None);
        }

        try
        {
            ended = (await receiving).MessageType == WebSocketMessageType.Close;
        }
        catch (WebSocketException)
        {
            ended = true;
        }

        ended.Should().BeTrue("the desktop does not go on for somebody who signed out");
    }

    [Fact]
    public async Task should_relay_what_the_client_of_the_desktop_reads_and_nothing_else_of_what_its_server_offers()
    {
        await using var desktop = await FakeDesktop.StartAsync();
        using var factory = new RelayFactory(desktop.Url);
        using var client = await factory.DesktopClientAsync();

        (await client.GetAsync("/browser/", Token)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("/browser/assets/index.js", Token)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("/browser/api/status", Token)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("/browser/api/websockets", Token)).StatusCode.Should().Be(HttpStatusCode.OK, "a handshake is a GET: the origin is judged when it is one");
        var refused = new[]
        {
            await client.GetAsync("/browser/api/files", Token),
            await client.GetAsync("/browser/api/", Token),
            await client.GetAsync("/browser/files/", Token),
            await client.GetAsync("/browser/Downloads/report.html", Token),
            await client.GetAsync("/browser/assets/", Token),
            await client.GetAsync("/browser/assets/sub/app.js", Token),
            await client.GetAsync("/browser/assets/app.html", Token),
            await client.GetAsync("/browser/devmode", Token),
            await client.GetAsync("/browser//api/files", Token),
            await client.GetAsync("/browser/api%2Ffiles", Token),
            await client.GetAsync("/browser/assets/%2e%2e/api/files", Token),
            await client.GetAsync("/browser/./api/files", Token),
            await client.GetAsync("/browser/pelorus/stream", Token),
            await client.PostAsync("/browser/api/clipboard", new StringContent("x"), Token),
            await client.PutAsync("/browser/assets/index.js", new StringContent("x"), Token),
            await client.DeleteAsync("/browser/assets/index.js", Token),
        };

        refused.Should().OnlyContain(response => response.StatusCode == HttpStatusCode.NotFound, "no write goes over HTTP, and the files and the other services of the image's web server are not the relay's");
        desktop.Seen.Select(seen => seen.Path).Should().BeEquivalentTo(["/browser/", "/browser/assets/index.js", "/browser/api/status", "/browser/api/websockets"]);
    }

    [Fact]
    public async Task should_end_an_open_websocket_when_the_namespace_becomes_exposed_after_it_was_opened()
    {
        await using var desktop = await FakeDesktop.StartAsync();
        using var factory = new RelayFactory(desktop.Url, movableClock: true);
        var (address, cookies) = await OpenRealAsync(factory);
        using var socket = await ConnectAsync(new Uri($"ws://{address.Authority}/browser/api/websockets"), address.GetLeftPart(UriPartial.Authority), cookies.Desktop);

        factory.Exposure.Ports.Add(3000);
        using var patience = CancellationTokenSource.CreateLinkedTokenSource(Token);
        patience.CancelAfter(TimeSpan.FromSeconds(30));
        var receiving = socket.ReceiveAsync(new byte[64], patience.Token);
        while (!receiving.IsCompleted && !patience.IsCancellationRequested)
        {
            factory.Clock!.Advance(TimeSpan.FromSeconds(6));
            await Task.Delay(50, CancellationToken.None);
        }

        var ended = false;
        try
        {
            ended = (await receiving).MessageType == WebSocketMessageType.Close;
        }
        catch (WebSocketException)
        {
            ended = true;
        }

        ended.Should().BeTrue("a desktop that others can reach is closed, the open sockets included");
    }

    [Fact]
    public async Task should_close_the_desktop_and_say_why_while_a_port_of_the_namespace_listens_off_the_loopback()
    {
        await using var desktop = await FakeDesktop.StartAsync();
        using var factory = new RelayFactory(desktop.Url);
        using var operatorClient = await factory.SignedInClientAsync();
        using var client = await factory.DesktopClientAsync(operatorClient);
        factory.Exposure.Ports.Add(3000);

        var closed = await client.GetAsync("/browser/", Token);
        (await client.GetAsync("/browser/api/websockets", Token)).StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable, "the WebSocket is as closed as the page");
        var health = await operatorClient.GetFromJsonAsync<Geminarr.Api.V1.Health.HealthResource>("/api/v1/health", Token);

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
    public async Task should_speak_to_the_desktop_with_the_one_origin_that_it_allows_whatever_the_page_of_the_person_is()
    {
        await using var desktop = await FakeDesktop.StartAsync();
        using var factory = new RelayFactory(desktop.Url);
        using var client = await factory.DesktopClientAsync();
        using var withOrigin = new HttpRequestMessage(HttpMethod.Get, "/browser/assets/index.js");
        withOrigin.Headers.Add("Origin", "http://localhost");

        (await client.SendAsync(withOrigin, Token)).EnsureSuccessStatusCode();
        (await client.GetAsync("/browser/", Token)).EnsureSuccessStatusCode();

        desktop.Origins.Should().OnlyContain(origin => origin == "https://geminarr.invalid", "the desktop allows this origin, which no web page can have");
    }

    [Fact]
    public async Task should_speak_to_the_desktop_by_its_own_address_whatever_the_name_the_person_reached_geminarr_by()
    {
        // The desktop's web server refuses every Host that is not the loopback (the init script of the image): the name of the person's address must not go through, or the desktop is closed to everyone but a person on localhost.
        await using var desktop = await FakeDesktop.StartAsync();
        using var factory = new RelayFactory(desktop.Url, allowedHostNames: "localhost;geminarr.homelab.example");
        using var client = await factory.DesktopClientAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/browser/");
        request.Headers.Host = "geminarr.homelab.example";

        (await client.SendAsync(request, Token)).EnsureSuccessStatusCode();

        desktop.Hosts.Should().OnlyContain(host => host == new Uri(desktop.Url).Authority, "the desktop is reached by the address that Geminarr gives it");
    }

    [Fact]
    public async Task should_not_give_the_desktop_what_a_proxy_in_front_of_geminarr_says_of_the_operator()
    {
        await using var desktop = await FakeDesktop.StartAsync();
        using var factory = new RelayFactory(desktop.Url);
        using var client = await factory.DesktopClientAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/browser/");
        request.Headers.Add("X-Forwarded-User", "operator");
        request.Headers.Add("Remote-User", "operator");
        request.Headers.Add("X-Authentik-Jwt", "a-token");
        request.Headers.Add("Accept-Language", "fr");

        (await client.SendAsync(request, Token)).EnsureSuccessStatusCode();

        desktop.ForeignHeaders.Should().Equal(false);
    }

    [Fact]
    public async Task should_say_bad_gateway_when_the_desktop_is_not_there_yet()
    {
        await using var desktop = await FakeDesktop.StartAsync();
        var gone = desktop.Url;
        await desktop.DisposeAsync();
        using var factory = new RelayFactory(gone);
        using var client = await factory.DesktopClientAsync();

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
        Geminarr.Host.Http.RelayRules.IsRelayPath(new PathString(path)).Should().Be(relay);
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

    /// <summary>The two cookies of an operator who is signed in over a real connection, as a browser would send them back, and the port of the interface (the desktop's is the factory's).</summary>
    private sealed record Cookies(string Operator, string Desktop, int MainPort);

    /// <summary>Starts the host on real ports (the interface's, and the desktop's), signs the operator in on the first and exchanges a ticket on the second. The address returned is the desktop's.</summary>
    private static async Task<(Uri Desktop, Cookies Cookies)> OpenRealAsync(RelayFactory factory)
    {
        factory.UseKestrel(options =>
        {
            options.Listen(System.Net.IPAddress.Loopback, 0);
            options.Listen(System.Net.IPAddress.Loopback, factory.DesktopPort);
        });
        factory.StartServer();
        var addresses = factory.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Select(address => new Uri(address)).ToList();
        var main = addresses.Single(address => address.Port != factory.DesktopPort);
        var desktop = addresses.Single(address => address.Port == factory.DesktopPort);

        using var client = new HttpClient { BaseAddress = main };
        using var login = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login") { Content = System.Net.Http.Json.JsonContent.Create(new { name = GeminarrFactoryBase.OperatorName, password = GeminarrFactoryBase.OperatorPassword }) };
        login.Headers.Add("X-Geminarr-Request", "1");
        using var response = await client.SendAsync(login, Token);
        var operatorCookie = response.Headers.GetValues("Set-Cookie").Single().Split(';')[0];

        using var ask = new HttpRequestMessage(HttpMethod.Post, "/api/v1/connection/desktop");
        ask.Headers.Add("X-Geminarr-Request", "1");
        ask.Headers.Add("Cookie", operatorCookie);
        using var asked = await client.SendAsync(ask, Token);
        var url = (await asked.Content.ReadFromJsonAsync<Geminarr.Api.V1.Connection.DesktopResource>(Token))!.Url;
        using var entering = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = desktop };
        using var entered = await entering.GetAsync(new Uri(url).PathAndQuery, Token);
        var desktopCookie = entered.Headers.GetValues("Set-Cookie").Single().Split(';')[0];
        return (desktop, new Cookies(operatorCookie, desktopCookie, main.Port));
    }

    private static async Task<string> DesktopCookieAsync(RelayFactory factory, HttpClient operatorClient)
    {
        var ticket = await RelayFactory.TicketAsync(operatorClient);
        using var anonymous = factory.Anonymous();
        using var entered = await anonymous.GetAsync($"/enter?ticket={ticket}", Token);
        return entered.Headers.GetValues("Set-Cookie").Single().Split(';')[0];
    }
}
