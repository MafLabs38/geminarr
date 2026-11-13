using Geminarr.Core.Pipeline;

namespace Geminarr.Core.Test.Pipeline;

public sealed class OptionsFixture
{
    [Fact]
    public void should_accept_the_defaults()
    {
        new ReaderOptionsValidator().Validate(null, new ReaderOptions()).Succeeded.Should().BeTrue();
        new RetentionOptionsValidator().Validate(null, new RetentionOptions()).Succeeded.Should().BeTrue();
    }

    [Theory]
    [InlineData("PollMinutes", 0)]
    [InlineData("MaxListed", 501)]
    [InlineData("MaxOpenPerPass", 0)]
    [InlineData("PassTimeoutMinutes", 121)]
    [InlineData("MaxBackoffMinutes", 0)]
    [InlineData("LoginFailures", 0)]
    [InlineData("DemoGrowSeconds", 0)]
    public void should_refuse_a_value_out_of_its_range_and_name_the_variable(string setting, int value)
    {
        var options = new ReaderOptions();
        typeof(ReaderOptions).GetProperty(setting)!.SetValue(options, value);

        var result = new ReaderOptionsValidator().Validate(null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain($"GEMINARR__READER__{setting.ToUpperInvariant()}");
    }

    [Fact]
    public void should_refuse_retention_out_of_range()
    {
        new RetentionOptionsValidator().Validate(null, new RetentionOptions { RunsDays = 0 }).FailureMessage.Should().Contain("GEMINARR__RETENTION__RUNSDAYS");
    }

    [Theory]
    [InlineData(20, 0, 360, 0.5, 20)]
    [InlineData(20, 1, 360, 0.5, 40)]
    [InlineData(20, 3, 360, 0.5, 160)]
    [InlineData(20, 10, 360, 0.5, 360)]
    [InlineData(20, 0, 360, 0, 16)]
    [InlineData(20, 0, 360, 1, 24)]
    [InlineData(20, -4, 360, 0.5, 20)]
    [InlineData(20, 99, 5, 0.5, 20)]
    public void should_wait_the_interval_doubled_for_each_failure_up_to_the_limit_with_a_jitter_of_twenty_percent(int poll, int failures, int max, double random, double minutes)
    {
        PassSchedule.NextDelay(poll, failures, max, random).TotalMinutes.Should().BeApproximately(minutes, 0.001);
    }

    [Theory]
    [InlineData(300, 300)]
    [InlineData(60, 60)]
    [InlineData(-5, 1)]
    [InlineData(0, 1)]
    [InlineData(1_200, 600)]
    [InlineData(86_400, 600)]
    public void should_wake_when_the_reading_is_due_and_at_least_every_ten_minutes_for_the_heartbeat(double secondsUntilReading, double expectedSeconds)
    {
        PassSchedule.WakeDelay(TimeSpan.FromSeconds(secondsUntilReading)).Should().Be(TimeSpan.FromSeconds(expectedSeconds));
    }
}
