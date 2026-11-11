using System.Net;
using System.Net.Http.Json;
using Geminarr.Api.V1.SystemInfo;

namespace Geminarr.Integration.Test;

[Collection("Host")]
public sealed class SystemApiFixture
{
    private readonly GeminarrFactory _factory;

    public SystemApiFixture(GeminarrFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task should_report_version_and_uptime_and_nothing_about_the_host()
    {
        var response = await _factory.CreateClient().GetAsync("/api/v1/system/status", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var status = await response.Content.ReadFromJsonAsync<SystemStatusResource>(TestContext.Current.CancellationToken);
        status!.Version.Should().NotBeNullOrWhiteSpace();
        status.UptimeSeconds.Should().BeGreaterThanOrEqualTo(0);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.Should().NotContain(_factory.DataFolder);
    }

    [Fact]
    public async Task should_not_cache_what_the_api_says_and_keep_the_headers_of_the_page()
    {
        var response = await _factory.CreateClient().GetAsync("/api/v1/system/status", TestContext.Current.CancellationToken);

        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        response.Headers.GetValues("X-Content-Type-Options").Should().Equal("nosniff");
        response.Headers.GetValues("Content-Security-Policy").Single().Should().Contain("frame-ancestors 'none'");
    }

    [Fact]
    public async Task should_answer_not_found_and_not_the_page_when_an_api_path_is_unknown()
    {
        var response = await _factory.CreateClient().GetAsync("/api/v1/nothing-here", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
