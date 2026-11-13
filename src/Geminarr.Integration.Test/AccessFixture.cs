using System.Net;
using System.Net.Http.Json;

namespace Geminarr.Integration.Test;

/// <summary>Who is let in, with what, who is refused: the matrix of access (every call but the probes needs a sign-in).</summary>
[Collection("Host")]
public sealed class AccessFixture
{
    private readonly GeminarrFactory _factory;

    public AccessFixture(GeminarrFactory factory)
    {
        _factory = factory;
    }

    [Theory]
    [InlineData("/api/v1/system/status")]
    [InlineData("/api/v1/health")]
    [InlineData("/api/v1/settings")]
    [InlineData("/api/v1/auth/session")]
    [InlineData("/openapi/v1.json")]
    [InlineData("/api/v1/nothing-here")]
    public async Task should_answer_401_to_a_caller_who_has_not_signed_in(string path)
    {
        var response = await _factory.CreateClient().GetAsync(path, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Headers.Location.Should().BeNull("an API answers with a status and never redirects to a login page");
    }

    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task should_let_the_probes_through_without_a_sign_in(string path)
    {
        var response = await _factory.CreateClient().GetAsync(path, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("/api/v1/system/status")]
    [InlineData("/api/v1/health")]
    [InlineData("/api/v1/settings")]
    [InlineData("/openapi/v1.json")]
    public async Task should_answer_a_caller_who_has_signed_in(string path)
    {
        using var client = await _factory.SignedInClientAsync();

        var response = await client.GetAsync(path, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task should_answer_404_and_not_the_page_to_an_unknown_api_path_once_signed_in()
    {
        using var client = await _factory.SignedInClientAsync();

        var response = await client.GetAsync("/api/v1/nothing-here", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task should_say_who_is_signed_in()
    {
        using var client = await _factory.SignedInClientAsync();

        var session = await client.GetFromJsonAsync<Geminarr.Api.V1.Access.SessionResource>("/api/v1/auth/session", TestContext.Current.CancellationToken);

        session!.Name.Should().Be(GeminarrFactoryBase.OperatorName);
    }

    [Fact]
    public async Task should_refuse_a_write_without_the_header_whoever_sends_it_even_the_sign_in_itself()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { name = GeminarrFactoryBase.OperatorName, password = GeminarrFactoryBase.OperatorPassword }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        response.Headers.Contains("Set-Cookie").Should().BeFalse();
    }

    [Fact]
    public async Task should_refuse_a_write_without_the_header_even_with_a_session()
    {
        using var client = await _factory.SignedInClientAsync(sendsTheHeader: false);

        var response = await client.PostAsync("/api/v1/auth/logout", content: null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.GetAsync("/api/v1/auth/session", TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.OK, "the refused write did not end the session");
    }

    [Theory]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    [InlineData("POST")]
    public async Task should_refuse_every_method_that_writes_without_the_header(string method)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), "/api/v1/settings");

        var response = await _factory.CreateClient().SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task should_answer_a_sign_in_without_a_name_the_same_as_a_wrong_one()
    {
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login") { Content = JsonContent.Create(new { }), Headers = { { "X-Geminarr-Request", "1" } } };

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await response.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ProblemDetails>(TestContext.Current.CancellationToken))!.Title.Should().Be("The name or the password is wrong.");
        response.Headers.Contains("Set-Cookie").Should().BeFalse();
    }

    [Fact]
    public async Task should_refuse_a_sign_in_that_is_not_json_and_give_no_cookie()
    {
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
        {
            Content = new StringContent("name=a&password=b", System.Text.Encoding.UTF8, "application/x-www-form-urlencoded"),
            Headers = { { "X-Geminarr-Request", "1" } },
        };

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType);
        response.Headers.Contains("Set-Cookie").Should().BeFalse();
    }

    [Fact]
    public async Task should_answer_401_to_a_sign_out_without_a_session_and_to_a_second_one()
    {
        using var nobody = _factory.CreateClient();
        nobody.DefaultRequestHeaders.Add("X-Geminarr-Request", "1");
        (await nobody.PostAsync("/api/v1/auth/logout", content: null, TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        using var client = await _factory.SignedInClientAsync();
        (await client.PostAsync("/api/v1/auth/logout", content: null, TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.PostAsync("/api/v1/auth/logout", content: null, TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task should_not_ask_the_header_of_a_read()
    {
        using var client = await _factory.SignedInClientAsync(sendsTheHeader: false);

        (await client.GetAsync("/api/v1/system/status", TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
