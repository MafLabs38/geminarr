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
}
