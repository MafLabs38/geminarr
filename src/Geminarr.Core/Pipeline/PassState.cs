namespace Geminarr.Core.Pipeline;

/// <summary>What happens in one pass: the worst outcome so far, with its reason, and the counts that the run will say.</summary>
internal sealed class PassState
{
    public string Outcome { get; private set; } = RunOutcomes.Ok;

    public string? Reason { get; private set; }

    public int Listed { get; set; }

    public int Opened { get; set; }

    public int TurnsSent { get; set; }

    public SinkFailure? Destination { get; set; }

    /// <summary>Says something went wrong; the worst outcome of the pass is the one that counts, with its reason.</summary>
    public void Fail(string outcome, string reason)
    {
        // The reason is the one of the worst outcome: a pass that was skipped and then failed to send says why it failed.
        if (Rank(outcome) > Rank(Outcome))
        {
            Outcome = outcome;
            Reason = reason;
        }
        else
        {
            Reason ??= reason;
        }
    }

    private static int Rank(string outcome) => outcome switch
    {
        RunOutcomes.Failed => 3,
        RunOutcomes.Partial => 2,
        RunOutcomes.Skipped => 1,
        _ => 0,
    };
}
