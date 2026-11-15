using System.Net;
using System.Net.Http.Json;
using Geminarr.Api.V1.Connection;

namespace Geminarr.Integration.Test;

/// <summary>The connection to the assistant through the API, with a browser in memory: the rules of access, the state, the page of the browser, the pause and the disconnect.</summary>
public sealed class ConnectionApiFixture
{
    private static readonly CancellationToken Token = TestContext.Current.CancellationToken;

    [Theory]
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
    public async Task should_give_the_address_of_the_page_of_the_browser_where_a_person_signs_in()
    {
        using var factory = new BrowserFactory();
        using var client = await factory.SignedInClientAsync();

        var connection = await client.GetFromJsonAsync<ConnectionResource>("/api/v1/connection", Token);

        connection!.HasBrowser.Should().BeTrue();
        connection.BrowserPageUrl.Should().Be("https://browser.homelab.example/");
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
