namespace Geminarr.Core.Pipeline;

/// <summary>Which conversations of a list are worth reading (SPEC §6.2): the one rule, for the pass that opens them and for whatever tells the reader which ones to bring.</summary>
public static class ConversationSelection
{
    /// <summary>
    /// A conversation is read when it is new, moved up the list, was renamed, or has turns that nothing is waiting to send; the most recent one is looked at every time (new turns do not move it up a list that it already heads).
    /// </summary>
    public static bool NeedsReading(ConversationHeader header, ConversationRecord? known, bool waitingToBeSent)
    {
        if (known is null)
        {
            return true;
        }

        return header.Rank == 0
            || header.Rank < known.ListRank
            || !string.Equals(ConversationLines.TitleHashOf(header.Title), known.TitleHash, StringComparison.Ordinal)
            || (known.TurnsKnown > known.TurnsSent && !waitingToBeSent);
    }

    /// <summary>
    /// The conversations below the ones that a pass looks at (<see cref="ReaderOptions.MaxListed"/>) that were never seen, oldest part of the list last: what the backfill brings next (SPEC §6.3), at most
    /// <see cref="ReaderOptions.BackfillPerPass"/> of them. <paramref name="isKnown"/> says whether Geminarr has seen the conversation; <paramref name="isHeldOrUnreadable"/> whether it waits to be taken or showed nothing.
    /// </summary>
    public static IEnumerable<ConversationHeader> ToBackfill(IEnumerable<ConversationHeader> headers, ReaderOptions options, Func<string, bool> isKnown, Func<ConversationHeader, bool> isHeldOrUnreadable) =>
        headers.Where(header => header.Rank >= options.MaxListed && !isKnown(header.Id) && !isHeldOrUnreadable(header)).Take(options.BackfillPerPass);
}
