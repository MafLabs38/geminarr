using System.Net;

namespace Geminarr.Integration.Test;

[Collection("Host")]
public sealed class SecurityHeadersFixture
{
    private readonly GeminarrFactory _factory;

    public SecurityHeadersFixture(GeminarrFactory factory)
    {
        _factory = factory;
    }

    [Theory]
    [InlineData("/api/v1/system/status", HttpStatusCode.OK)]
    [InlineData("/API/v1/system/status", HttpStatusCode.OK)]
    [InlineData("/api/v1/nothing-here", HttpStatusCode.NotFound)]
    [InlineData("/openapi/v1.json", HttpStatusCode.OK)]
    public async Task should_keep_the_security_headers_and_cache_nothing_on_what_the_api_says_even_when_it_says_no(string path, HttpStatusCode expected)
    {
        var response = await _factory.CreateClient().GetAsync(path, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(expected);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        response.Headers.GetValues("Pragma").Should().Equal("no-cache");
        response.Headers.GetValues("X-Content-Type-Options").Should().Equal("nosniff");
        response.Headers.GetValues("X-Frame-Options").Should().Equal("DENY");
        response.Headers.GetValues("Referrer-Policy").Should().Equal("no-referrer");
        response.Headers.GetValues("Content-Security-Policy").Single().Should().Contain("frame-ancestors 'none'");
    }

    [Fact]
    public async Task should_keep_the_security_headers_on_the_probes_which_are_not_the_api()
    {
        var response = await _factory.CreateClient().GetAsync("/health/live", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.GetValues("X-Content-Type-Options").Should().Equal("nosniff");
        response.Headers.GetValues("Content-Security-Policy").Single().Should().Contain("default-src 'self'");
    }
}
