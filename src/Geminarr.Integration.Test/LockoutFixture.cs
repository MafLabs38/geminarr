using System.Net;
using System.Net.Http.Json;

namespace Geminarr.Integration.Test;

/// <summary>Five wrong passwords lock the name for a while, the right one included; the answer to a wrong name and to a wrong password is the same.</summary>
public sealed class LockoutFixture : IDisposable
{
    // A host of its own for each test: they fail sign-ins on purpose, and a lockout is state of the host.
    private readonly LockoutFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    private static HttpRequestMessage Login(string name, string password) =>
        new(HttpMethod.Post, "/api/v1/auth/login")
        {
            Content = JsonContent.Create(new { name, password }),
            Headers = { { "X-Geminarr-Request", "1" } },
        };

    [Fact]
    public async Task should_tell_a_wrong_name_and_a_wrong_password_the_same_way()
    {
        using var client = _factory.CreateClient();

        var wrongName = await client.SendAsync(Login("someone-else", GeminarrFactoryBase.OperatorPassword), TestContext.Current.CancellationToken);
        var wrongPassword = await client.SendAsync(Login(GeminarrFactoryBase.OperatorName, "wrong password"), TestContext.Current.CancellationToken);

        wrongName.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        wrongPassword.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var nameProblem = await wrongName.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ProblemDetails>(TestContext.Current.CancellationToken);
        var passwordProblem = await wrongPassword.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ProblemDetails>(TestContext.Current.CancellationToken);
        nameProblem!.Title.Should().Be(passwordProblem!.Title).And.Be("The name or the password is wrong.");
        wrongName.Headers.Contains("Set-Cookie").Should().BeFalse();
    }

    [Fact]
    public async Task should_lock_the_name_after_five_wrong_passwords_and_say_when_to_come_back()
    {
        using var client = _factory.CreateClient();
        for (var attempt = 0; attempt < 5; attempt++)
        {
            (await client.SendAsync(Login("locked-name", "wrong password"), TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        var locked = await client.SendAsync(Login("locked-name", "wrong password"), TestContext.Current.CancellationToken);

        locked.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        locked.Headers.RetryAfter!.Delta.Should().BeGreaterThan(TimeSpan.Zero);
    }

    [Fact]
    public async Task should_refuse_the_right_password_of_the_operator_once_the_name_is_locked()
    {
        using var client = _factory.CreateClient();
        for (var attempt = 0; attempt < 5; attempt++)
        {
            await client.SendAsync(Login(GeminarrFactoryBase.OperatorName, "wrong password"), TestContext.Current.CancellationToken);
        }

        var response = await client.SendAsync(Login(GeminarrFactoryBase.OperatorName, GeminarrFactoryBase.OperatorPassword), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        response.Headers.Contains("Set-Cookie").Should().BeFalse();
    }
}
