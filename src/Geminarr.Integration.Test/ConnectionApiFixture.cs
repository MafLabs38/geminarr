using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using Geminarr.Api.V1.Connection;

namespace Geminarr.Integration.Test;

/// <summary>The connection to the assistant through the API and the live view over a WebSocket, with a browser in memory: the rules of access, one viewer, the ticket, the input, the sign-in, the pause and the disconnect.</summary>
public sealed class ConnectionApiFixture
{
    private static readonly CancellationToken Token = TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("POST", "/api/v1/connection/live-views")]
    [InlineData("DELETE", "/api/v1/connection/live-views")]
    [InlineData("POST", "/api/v1/connection/pause")]
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
    public async Task should_say_there_is_nothing_to_sign_in_to_when_no_browser_reads()
    {
        using var factory = new GeminarrFactory();
        using var client = await factory.SignedInClientAsync();

        var response = await client.PostAsync("/api/v1/connection/live-views", content: null, Token);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.GetFromJsonAsync<ConnectionResource>("/api/v1/connection", Token))!.HasBrowser.Should().BeFalse();
    }

    [Fact]
    public async Task should_give_a_ticket_to_one_live_view_at_a_time_and_close_it_on_request()
    {
        using var factory = new BrowserFactory();
        using var client = await factory.SignedInClientAsync();

        var response = await client.PostAsync("/api/v1/connection/live-views", content: null, Token);
        var ticket = (await response.Content.ReadFromJsonAsync<LiveViewResource>(Token))!;

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        ticket.Path.Should().Be($"/api/v1/connection/live-views/{ticket.Id}");
        (ticket.Width, ticket.Height).Should().Be((1280, 800));
        ticket.Token.Length.Should().BeGreaterThan(40);
        (await client.GetFromJsonAsync<ConnectionResource>("/api/v1/connection", Token))!.State.Should().Be("Connecting");
        (await client.PostAsync("/api/v1/connection/live-views", content: null, Token)).StatusCode.Should().Be(HttpStatusCode.Conflict);

        (await client.DeleteAsync("/api/v1/connection/live-views", Token)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await client.GetFromJsonAsync<ConnectionResource>("/api/v1/connection", Token))!.State.Should().Be("NotConnected");
        (await client.PostAsync("/api/v1/connection/live-views", content: null, Token)).StatusCode.Should().Be(HttpStatusCode.OK);
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
        (await client.PostAsync("/api/v1/connection/live-views", content: null, Token)).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var resumed = await (await client.PostAsync("/api/v1/connection/resume", content: null, Token)).Content.ReadFromJsonAsync<ConnectionResource>(Token);
        resumed!.State.Should().Be("NotConnected");

        var untilResumed = await (await client.PostAsJsonAsync("/api/v1/connection/pause", new { hours = (double?)null }, Token)).Content.ReadFromJsonAsync<ConnectionResource>(Token);
        untilResumed!.PausedUntil.Should().BeNull();
        var disconnected = await (await client.PostAsync("/api/v1/connection/disconnect", content: null, Token)).Content.ReadFromJsonAsync<ConnectionResource>(Token);
        disconnected!.State.Should().Be("NotConnected");
        factory.Browser.Destroyed.Should().Be(1);
    }

    [Fact]
    public async Task should_show_the_pictures_of_the_browser_and_take_its_input_over_the_socket()
    {
        using var factory = new BrowserFactory();
        using var client = await factory.SignedInClientAsync();
        var ticket = (await (await client.PostAsync("/api/v1/connection/live-views", content: null, Token)).Content.ReadFromJsonAsync<LiveViewResource>(Token))!;

        using var socket = await ConnectAsync(factory, ticket, ticket.Token);

        (await ReadTextAsync(socket)).Should().Be("""{"t":"ready","width":1280,"height":800}""");
        factory.Browser.View!.Show([0xFF, 0xD8, 0xFF, 0xD9]);
        var picture = await ReadAsync(socket);
        picture.Type.Should().Be(WebSocketMessageType.Binary);
        picture.Bytes.Should().Equal(0xFF, 0xD8, 0xFF, 0xD9);

        await SendAsync(socket, """{"t":"down","x":10,"y":20,"button":"left"}""");
        await SendAsync(socket, "not an input");
        await SendAsync(socket, """{"t":"move","x":99999,"y":1}""");
        await SendAsync(socket, """{"t":"keydown","key":"a"}""");
        await SendAsync(socket, """{"t":"text","text":"pasted"}""");
        await EventuallyAsync(() => factory.Browser.View.Inputs().Count >= 3);

        factory.Browser.View.Inputs().Select(input => (input.Kind.ToString(), input.Key ?? input.Text ?? input.X.ToString(System.Globalization.CultureInfo.InvariantCulture))).Should().Equal(("MouseDown", "10"), ("KeyDown", "a"), ("Text", "pasted"));
    }

    [Fact]
    public async Task should_be_connected_and_close_by_itself_when_the_browser_is_on_the_assistants_page_if_the_page_is_what_says_it()
    {
        using var factory = new BrowserFactory(detectByPage: true);
        using var client = await factory.SignedInClientAsync();
        var ticket = (await (await client.PostAsync("/api/v1/connection/live-views", content: null, Token)).Content.ReadFromJsonAsync<LiveViewResource>(Token))!;
        using var socket = await ConnectAsync(factory, ticket, ticket.Token);
        await ReadTextAsync(socket);

        factory.Browser.View!.Page = Geminarr.Core.Browser.PageKind.Assistant;

        (await ReadTextAsync(socket, timeoutSeconds: 10)).Should().Be("""{"t":"signed-in"}""");
        var connection = await client.GetFromJsonAsync<ConnectionResource>("/api/v1/connection", Token);
        connection!.State.Should().Be("Connected");
        factory.Browser.Profile().HasSession.Should().BeTrue();
        await EventuallyAsync(() => factory.Browser.View.Disposed);
        (await client.PostAsync("/api/v1/connection/live-views", content: null, Token)).StatusCode.Should().Be(HttpStatusCode.OK, "the viewer is gone: another live view may be asked for");
    }

    [Fact]
    public async Task should_not_decide_from_the_address_alone_and_wait_for_the_operator_to_say_they_are_signed_in()
    {
        using var factory = new BrowserFactory();
        using var client = await factory.SignedInClientAsync();
        var ticket = (await (await client.PostAsync("/api/v1/connection/live-views", content: null, Token)).Content.ReadFromJsonAsync<LiveViewResource>(Token))!;
        using var socket = await ConnectAsync(factory, ticket, ticket.Token);
        await ReadTextAsync(socket);
        factory.Browser.View!.Page = Geminarr.Core.Browser.PageKind.Assistant;
        await Task.Delay(TimeSpan.FromSeconds(3), Token);
        (await client.GetFromJsonAsync<ConnectionResource>("/api/v1/connection", Token))!.State.Should().Be("Connecting", "the assistant's page may be shown to somebody who is not signed in");

        await SendAsync(socket, "{\"t\":\"done\"}");

        (await ReadTextAsync(socket)).Should().Be("{\"t\":\"signed-in\"}");
        (await client.GetFromJsonAsync<ConnectionResource>("/api/v1/connection", Token))!.State.Should().Be("Connected");
        factory.Browser.Profile().HasSession.Should().BeTrue();
    }

    [Fact]
    public async Task should_say_not_yet_when_the_operator_says_they_are_signed_in_and_the_browser_is_still_on_a_sign_in_page()
    {
        using var factory = new BrowserFactory();
        using var client = await factory.SignedInClientAsync();
        var ticket = (await (await client.PostAsync("/api/v1/connection/live-views", content: null, Token)).Content.ReadFromJsonAsync<LiveViewResource>(Token))!;
        using var socket = await ConnectAsync(factory, ticket, ticket.Token);
        await ReadTextAsync(socket);

        await SendAsync(socket, "{\"t\":\"done\"}");

        (await ReadTextAsync(socket)).Should().Be("{\"t\":\"not-yet\"}");
        (await client.GetFromJsonAsync<ConnectionResource>("/api/v1/connection", Token))!.State.Should().Be("Connecting");
        factory.Browser.Profile().HasSession.Should().BeFalse();
    }

    [Fact]
    public async Task should_go_back_when_the_page_closes_without_a_session()
    {
        using var factory = new BrowserFactory();
        using var client = await factory.SignedInClientAsync();
        var ticket = (await (await client.PostAsync("/api/v1/connection/live-views", content: null, Token)).Content.ReadFromJsonAsync<LiveViewResource>(Token))!;
        var socket = await ConnectAsync(factory, ticket, ticket.Token);
        await ReadTextAsync(socket);

        await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", Token);
        socket.Dispose();

        await EventuallyAsync(() => factory.Browser.View!.Disposed);
        await EventuallyAsync(() => client.GetFromJsonAsync<ConnectionResource>("/api/v1/connection", Token).GetAwaiter().GetResult()!.State == "NotConnected");
    }

    [Fact]
    public async Task should_close_the_live_view_when_it_is_closed_by_the_operator_over_http()
    {
        using var factory = new BrowserFactory();
        using var client = await factory.SignedInClientAsync();
        var ticket = (await (await client.PostAsync("/api/v1/connection/live-views", content: null, Token)).Content.ReadFromJsonAsync<LiveViewResource>(Token))!;
        using var socket = await ConnectAsync(factory, ticket, ticket.Token);
        await ReadTextAsync(socket);

        await client.DeleteAsync("/api/v1/connection/live-views", Token);

        await EventuallyAsync(() => factory.Browser.View!.Disposed);
    }

    [Fact]
    public async Task should_refuse_the_same_host_on_another_port_and_accept_the_page_that_is_on_https_behind_a_proxy()
    {
        using var factory = new BrowserFactory();
        using var client = await factory.SignedInClientAsync();
        var ticket = (await (await client.PostAsync("/api/v1/connection/live-views", content: null, Token)).Content.ReadFromJsonAsync<LiveViewResource>(Token))!;

        var otherPort = async () => await ConnectAsync(factory, ticket, ticket.Token, origin: "http://localhost:9999");
        await otherPort.Should().ThrowAsync<Exception>();
        using var behindTheProxy = await ConnectAsync(factory, ticket, ticket.Token, origin: "https://localhost");

        behindTheProxy.State.Should().Be(WebSocketState.Open, "the proxy ends TLS: the server sees http, the page is on https, and the host is the same");
    }

    [Fact]
    public async Task should_refuse_a_viewer_without_the_right_token_or_from_another_origin_or_without_a_session()
    {
        using var factory = new BrowserFactory();
        using var client = await factory.SignedInClientAsync();
        var ticket = (await (await client.PostAsync("/api/v1/connection/live-views", content: null, Token)).Content.ReadFromJsonAsync<LiveViewResource>(Token))!;

        var wrongOrigin = async () => await ConnectAsync(factory, ticket, ticket.Token, origin: "http://evil.example");
        var noOrigin = async () => await ConnectAsync(factory, ticket, ticket.Token, origin: null);
        var noSession = async () => await ConnectAsync(factory, ticket, ticket.Token, cookie: null);
        var wrongToken = async () => await ConnectAsync(factory, ticket, "not-the-token");

        await wrongOrigin.Should().ThrowAsync<Exception>();
        await noOrigin.Should().ThrowAsync<Exception>();
        await noSession.Should().ThrowAsync<Exception>();
        await wrongToken.Should().ThrowAsync<Exception>();
        factory.Browser.View.Should().BeNull("the browser is never started for a viewer that was refused");
    }

    [Fact]
    public async Task should_refuse_a_request_that_is_not_a_websocket_and_a_token_that_was_used()
    {
        using var factory = new BrowserFactory();
        using var client = await factory.SignedInClientAsync();
        var ticket = (await (await client.PostAsync("/api/v1/connection/live-views", content: null, Token)).Content.ReadFromJsonAsync<LiveViewResource>(Token))!;

        (await client.GetAsync($"{ticket.Path}?token={ticket.Token}", Token)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var first = await ConnectAsync(factory, ticket, ticket.Token);
        var again = async () => await ConnectAsync(factory, ticket, ticket.Token);

        await again.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task should_tell_the_page_when_the_browser_could_not_be_started()
    {
        using var factory = new BrowserFactory();
        factory.Browser.FailToOpen = true;
        using var client = await factory.SignedInClientAsync();
        var ticket = (await (await client.PostAsync("/api/v1/connection/live-views", content: null, Token)).Content.ReadFromJsonAsync<LiveViewResource>(Token))!;
        using var socket = await ConnectAsync(factory, ticket, ticket.Token);

        (await ReadTextAsync(socket)).Should().Contain("\"t\":\"error\"").And.NotContain("InvalidOperation");
        await EventuallyAsync(() => client.GetFromJsonAsync<ConnectionResource>("/api/v1/connection", Token).GetAwaiter().GetResult()!.State == "NotConnected");
    }

    private static async Task<WebSocket> ConnectAsync(BrowserFactory factory, LiveViewResource ticket, string token, string? origin = "http://localhost", string? cookie = "yes")
    {
        var web = factory.Server.CreateWebSocketClient();
        var session = cookie is null ? null : await factory.SessionCookieAsync();
        web.ConfigureRequest = request =>
        {
            if (origin is not null)
            {
                request.Headers["Origin"] = origin;
            }

            if (session is not null)
            {
                request.Headers["Cookie"] = session;
            }
        };
        return await web.ConnectAsync(new Uri($"ws://localhost{ticket.Path}?token={Uri.EscapeDataString(token)}"), Token);
    }

    private static async Task<(WebSocketMessageType Type, byte[] Bytes)> ReadAsync(WebSocket socket, int timeoutSeconds = 10)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        var buffer = new byte[16 * 1024];
        var received = await socket.ReceiveAsync(buffer, timeout.Token);
        return (received.MessageType, buffer[..received.Count]);
    }

    private static async Task<string> ReadTextAsync(WebSocket socket, int timeoutSeconds = 10)
    {
        // Pictures may come first: the text messages are what is waited for.
        while (true)
        {
            var (type, bytes) = await ReadAsync(socket, timeoutSeconds);
            if (type == WebSocketMessageType.Text)
            {
                return Encoding.UTF8.GetString(bytes);
            }
        }
    }

    private static Task SendAsync(WebSocket socket, string text) => socket.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, endOfMessage: true, Token);

    private static async Task EventuallyAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 200 && !condition(); attempt++)
        {
            await Task.Delay(50, Token);
        }

        condition().Should().BeTrue("it should have happened within ten seconds");
    }
}
