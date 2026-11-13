namespace Geminarr.Core.Pipeline;

/// <summary>How long to wait between two passes: the interval, doubled for each pass that failed in a row up to a limit, with a random 20 % either way so that passes do not fall on the minute.</summary>
public static class PassSchedule
{
    /// <summary>The longest wait between two passes: Mnemarr calls an always-on machine silent after 30 minutes without a heartbeat, so slow reading never means a slow heartbeat (SPEC §5.5, §6.4).</summary>
    public static readonly TimeSpan LongestWake = TimeSpan.FromMinutes(10);

    /// <summary>The shortest wake, so that a reading that is just due is not a busy loop.</summary>
    public static readonly TimeSpan ShortestWake = TimeSpan.FromSeconds(1);

    /// <summary>How long to sleep before the next pass: until the reading is due, but never longer than <see cref="LongestWake"/>.</summary>
    public static TimeSpan WakeDelay(TimeSpan untilReading) => TimeSpan.FromTicks(Math.Clamp(untilReading.Ticks, ShortestWake.Ticks, LongestWake.Ticks));


    public static TimeSpan NextDelay(int pollMinutes, int failuresInARow, int maxBackoffMinutes, double randomUnit)
    {
        var backoff = pollMinutes * Math.Pow(2, Math.Min(Math.Max(failuresInARow, 0), 12));
        var minutes = Math.Min(backoff, Math.Max(maxBackoffMinutes, pollMinutes));
        var jitter = 1 + ((Math.Clamp(randomUnit, 0, 1) * 0.4) - 0.2);
        return TimeSpan.FromMinutes(minutes * jitter);
    }
}
