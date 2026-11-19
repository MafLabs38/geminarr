using System.Net;
using System.Net.Http.Json;

namespace Geminarr.Integration.Test;

/// <summary>A container with nobody configured is alive and closed: the platform does not restart it, and nobody can get in.</summary>
public sealed class NoOperatorFixture : IClassFixture<NoOperatorFactory>
{
    private readonly NoOperatorFactory _factory;

    public NoOperatorFixture(NoOperatorFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task should_stay_ready_for_the_platform_and_closed_for_everybody()
    {
        using var client = _factory.CreateClient();

        (await client.GetAsync("/health/ready", TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("/api/v1/system/status", TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task should_let_nobody_in_whatever_name_and_password_are_tried()
    {
        using var client = _factory.CreateClient();
        using var login = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
        {
            Content = JsonContent.Create(new { name = GeminarrFactoryBase.OperatorName, password = GeminarrFactoryBase.OperatorPassword }),
            Headers = { { "X-Geminarr-Request", "1" } },
        };

        var response = await client.SendAsync(login, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Headers.Contains("Set-Cookie").Should().BeFalse();
    }
}
