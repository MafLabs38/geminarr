using System.Net;
using System.Net.Http.Json;

namespace Geminarr.Integration.Test;

/// <summary>The life of a session: the cookie it is given, signing out, and the end of its lifetime.</summary>
public sealed class SessionFixture : IClassFixture<ShortSessionFactory>, IClassFixture<SecureCookieFactory>
{
    private readonly ShortSessionFactory _short;
    private readonly SecureCookieFactory _secure;

    public SessionFixture(ShortSessionFactory shortSession, SecureCookieFactory secure)
    {
        _short = shortSession;
        _secure = secure;
    }

    private static HttpRequestMessage Login(string password) =>
        new(HttpMethod.Post, "/api/v1/auth/login")
        {
            Content = JsonContent.Create(new { name = GeminarrFactoryBase.OperatorName, password }),
            Headers = { { "X-Geminarr-Request", "1" } },
        };

    [Fact]
    public async Task should_give_a_cookie_that_script_cannot_read_and_another_site_cannot_send()
    {
        using var client = _short.CreateClient();

        var response = await client.SendAsync(Login(GeminarrFactoryBase.OperatorPassword), TestContext.Current.CancellationToken);

        var cookie = response.Headers.GetValues("Set-Cookie").Single();
        cookie.Should().StartWith("geminarr.session=").And.Contain("httponly").And.Contain("samesite=strict").And.NotContain("domain=");
        cookie.Should().NotContain("expires=", "it is a session cookie: it goes with the browser, and the server ends it anyway");
    }

    [Fact]
    public async Task should_give_a_secure_cookie_with_the_host_prefix_by_default()
    {
        using var client = _secure.CreateClient();

        var response = await client.SendAsync(Login(GeminarrFactoryBase.OperatorPassword), TestContext.Current.CancellationToken);

        response.Headers.GetValues("Set-Cookie").Single().Should().StartWith("__Host-geminarr.session=").And.Contain("secure").And.Contain("path=/");
    }

    [Fact]
    public async Task should_make_a_copy_of_the_cookie_dead_as_soon_as_the_operator_signs_out()
    {
        var cookie = await CookieOfAsync(_short);
        using var browser = CopyWith(cookie);
        using var attacker = CopyWith(cookie);
        (await attacker.GetAsync("/api/v1/auth/session", TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.OK);

        using var logout = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/logout") { Headers = { { "X-Geminarr-Request", "1" } } };
        (await browser.SendAsync(logout, TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await browser.GetAsync("/api/v1/auth/session", TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await attacker.GetAsync("/api/v1/auth/session", TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "the session is gone on the server, not only dropped by the browser");
    }

    [Fact]
    public async Task should_end_a_session_at_its_lifetime_even_if_it_is_used_all_along()
    {
        using var client = await _short.SignedInClientAsync();
        _short.Clock!.Advance(TimeSpan.FromMinutes(119));
        (await client.GetAsync("/api/v1/auth/session", TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.OK);

        _short.Clock.Advance(TimeSpan.FromMinutes(2));

        (await client.GetAsync("/api/v1/auth/session", TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task should_not_accept_a_cookie_that_was_made_up()
    {
        using var forged = CopyWith("geminarr.session=CfDJ8Zm9yZ2VkLWNvb2tpZQ");

        (await forged.GetAsync("/api/v1/auth/session", TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>The cookie a fresh sign-in gives, read from the answer as a browser would keep it.</summary>
    private static async Task<string> CookieOfAsync(GeminarrFactoryBase factory)
    {
        using var client = factory.CreateClient();
        using var response = await client.SendAsync(Login(GeminarrFactoryBase.OperatorPassword), TestContext.Current.CancellationToken);
        return response.Headers.GetValues("Set-Cookie").Single().Split(';')[0];
    }

    private HttpClient CopyWith(string cookie)
    {
        var client = new HttpClient(_short.Server.CreateHandler()) { BaseAddress = new Uri("http://localhost/") };
        client.DefaultRequestHeaders.Add("Cookie", cookie);
        return client;
    }
}
