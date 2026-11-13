namespace Geminarr.Core.Pipeline;

public interface IRunStore
{
    long Start(string kind, DateTimeOffset at);

    void Finish(long id, DateTimeOffset at, RunOutcome outcome);

    /// <summary>The latest run of a kind, or nothing.</summary>
    RunRecord? Latest(string kind);

    /// <summary>The latest runs, newest first.</summary>
    IReadOnlyList<RunRecord> Recent(int limit);

    /// <summary>Forgets what ended before a moment: the runs are kept for a while, not for ever.</summary>
    void Prune(DateTimeOffset before);
}
