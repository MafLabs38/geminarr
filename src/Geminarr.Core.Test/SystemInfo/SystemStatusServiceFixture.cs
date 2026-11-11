using Geminarr.Core.SystemInfo;
using Microsoft.Extensions.Time.Testing;

namespace Geminarr.Core.Test.SystemInfo;

public sealed class SystemStatusServiceFixture
{
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 11, 8, 0, 0, TimeSpan.Zero));

    [Fact]
    public void should_report_the_start_and_the_uptime_when_time_passes()
    {
        var subject = new SystemStatusService(_clock);
        _clock.Advance(TimeSpan.FromMinutes(90));

        var status = subject.GetStatus();

        status.StartedAt.Should().Be(new DateTimeOffset(2026, 11, 11, 8, 0, 0, TimeSpan.Zero));
        status.Uptime.Should().Be(TimeSpan.FromMinutes(90));
    }
}
