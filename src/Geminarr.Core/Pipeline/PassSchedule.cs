namespace Geminarr.Core.Pipeline;

/// <summary>How long to wait between two passes: the interval, doubled for each pass that failed in a row up to a limit, with a random 20 % either way so that passes do not fall on the minute.</summary>
public static class PassSchedule
{
    public static TimeSpan NextDelay(int pollMinutes, int failuresInARow, int maxBackoffMinutes, double randomUnit)
    {
        var backoff = pollMinutes * Math.Pow(2, Math.Min(Math.Max(failuresInARow, 0), 12));
        var minutes = Math.Min(backoff, Math.Max(maxBackoffMinutes, pollMinutes));
        var jitter = 1 + ((Math.Clamp(randomUnit, 0, 1) * 0.4) - 0.2);
        return TimeSpan.FromMinutes(minutes * jitter);
    }
}
