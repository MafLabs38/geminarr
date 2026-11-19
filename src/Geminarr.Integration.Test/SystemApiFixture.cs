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
    public async Task should_report_version_runtime_start_and_uptime_and_nothing_about_the_host()
    {
        using var client = await _factory.SignedInClientAsync();
        var response = await client.GetAsync("/api/v1/system/status", TestContext.Current.CancellationToken);
        var now = DateTimeOffset.UtcNow;

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var status = await response.Content.ReadFromJsonAsync<SystemStatusResource>(TestContext.Current.CancellationToken);
        status!.Version.Should().NotBeNullOrWhiteSpace();
        status.RuntimeVersion.Should().StartWith(".NET");
        status.StartedAt.Should().BeBefore(now).And.BeAfter(now.AddHours(-1));
        status.UptimeSeconds.Should().BeGreaterThanOrEqualTo(0);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.Should().NotContain(_factory.DataFolder);
    }
}
