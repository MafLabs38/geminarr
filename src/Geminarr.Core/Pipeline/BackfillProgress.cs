namespace Geminarr.Core.Pipeline;

/// <summary>
/// How far the history has been walked (SPEC §6.3), told from what the page listed and what Geminarr has seen: there is no flag that says "done", so it cannot disagree with the list.
/// <see cref="Listed"/> is what the last list held; <see cref="Known"/> how many of them Geminarr has read; <see cref="ListComplete"/> whether the page said that the list ends there.
/// </summary>
public sealed record BackfillProgress(bool Enabled, int PerPass, int Listed, int Known, bool ListComplete)
{
    /// <summary>Every conversation of a list that reaches its end has been read.</summary>
    public bool Done => ListComplete && Known >= Listed && Listed > 0;

    public static BackfillProgress Of(IReadOnlyCollection<ConversationHeader> listed, Func<string, bool> isKnown, ReaderOptions options, bool listComplete) =>
        new(options.BackfillPerPass > 0, options.BackfillPerPass, listed.Count, listed.Count(header => isKnown(header.Id)), listComplete);
}
