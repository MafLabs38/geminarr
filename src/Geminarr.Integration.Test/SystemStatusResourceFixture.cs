using Geminarr.Api.V1.SystemInfo;
using Geminarr.Core.SystemInfo;

namespace Geminarr.Integration.Test;

public sealed class SystemStatusResourceFixture
{
    [Fact]
    public void should_give_the_uptime_in_whole_seconds_and_keep_the_other_fields()
    {
        var startedAt = new DateTimeOffset(2026, 11, 11, 8, 0, 0, TimeSpan.Zero);

        var resource = new SystemStatus("0.1.0.7", ".NET 10", startedAt, TimeSpan.FromSeconds(61.9)).ToResource();

        resource.Should().Be(new SystemStatusResource("0.1.0.7", ".NET 10", startedAt, 61));
    }
}
