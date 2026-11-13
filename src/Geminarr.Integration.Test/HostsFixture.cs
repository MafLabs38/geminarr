using System.Net;

namespace Geminarr.Integration.Test;

/// <summary>A request for a name the page may not be reached by is refused (DNS rebinding), except the probes, which a platform calls by the address of the pod.</summary>
[Collection("Host")]
public sealed class HostsFixture
{
    private readonly GeminarrFactory _factory;

    public HostsFixture(GeminarrFactory factory)
    {
        _factory = factory;
    }

    private HttpClient ClientFor(string host)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Host = host;
        return client;
    }

    [Theory]
    [InlineData("evil.example")]
    [InlineData("evil.example:8282")]
    [InlineData("localhost.evil.example")]
    [InlineData("192.168.1.10:8282")]
    public async Task should_refuse_a_name_that_is_not_in_the_list(string host)
    {
        using var client = ClientFor(host);

        foreach (var path in new[] { "/api/v1/system/status", "/", "/api/v1/auth/session" })
        {
            var response = await client.GetAsync(path, TestContext.Current.CancellationToken);

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest, path);
            response.Headers.GetValues("X-Content-Type-Options").Should().Equal("nosniff");
        }
    }

    [Theory]
    [InlineData("localhost")]
    [InlineData("localhost:8282")]
    [InlineData("127.0.0.1:8282")]
    public async Task should_answer_a_name_that_is_in_the_list(string host)
    {
        using var client = ClientFor(host);

        (await client.GetAsync("/api/v1/system/status", TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "the name is allowed: it is the sign-in that answers");
    }

    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task should_answer_the_probes_whatever_the_name_they_are_called_by(string path)
    {
        using var client = ClientFor("10.42.0.17:8282");

        (await client.GetAsync(path, TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
