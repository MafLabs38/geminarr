using Geminarr.Core.Pipeline;

namespace Geminarr.Core.Extension;

/// <summary>
/// What the inbox knows about the conversations that showed no turn. Three structures, one rule each, and no lock of its own (the inbox's is the one that holds):
/// the <b>marks</b> (a conversation that is still as it was, for the version of the extension that saw it, is not asked for again; neither a read nor an hour clears one),
/// the <b>run</b> (the ones since the last read; three within the hour are a page that is not what the extension knows), and what is <b>held aside</b>
/// (the marks of such a run, and those made while the page is broken: evidence about the page, not about the conversations, they become marks only when the same version then reads a conversation).
/// </summary>
internal sealed class UnreadableLedger
{
    private readonly Dictionary<string, Mark> _marks = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTimeOffset> _run = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Mark> _aside = new(StringComparer.Ordinal);
    private readonly int _bound;
    private readonly TimeSpan _runLasts;

    public UnreadableLedger(int bound, TimeSpan runLasts)
    {
        _bound = bound;
        _runLasts = runLasts;
    }

    /// <summary>Says whether the run has reached <paramref name="toBreak"/>: the page is then not what the extension knows.</summary>
    public bool Record(string id, ConversationHeader? header, string? version, DateTimeOffset now, bool pageIsBroken, int toBreak)
    {
        var mark = new Mark(now, header?.Title, header?.Rank ?? 0, version);
        _run[id] = now;
        (pageIsBroken ? _aside : _marks)[id] = mark;
        Trim();
        if (_run.Count < toBreak)
        {
            return false;
        }

        foreach (var runId in _run.Keys)
        {
            if (_marks.Remove(runId, out var held))
            {
                _aside[runId] = held;
            }
        }

        return true;
    }

    /// <summary>A conversation was read by this version: the run starts again, and what was held aside by the same version is believed.</summary>
    public void Read(string? version)
    {
        _run.Clear();
        foreach (var (id, mark) in _aside.Where(pair => pair.Value.Version == version).ToList())
        {
            _marks[id] = mark;
        }

        _aside.Clear();
    }

    /// <summary>The most recent conversation is never excluded (it cannot move up, and is looked at every time); a new version of the extension asks again for every one.</summary>
    public bool Holds(ConversationHeader header, string? version) =>
        header.Rank > 0 && _marks.TryGetValue(header.Id, out var mark) && mark.Version == version && mark.Title == header.Title && header.Rank >= mark.Rank;

    public void Clear()
    {
        _marks.Clear();
        _run.Clear();
        _aside.Clear();
    }

    /// <summary>Only the run, and what is held aside, expire: three conversations that showed nothing hours apart are not a page that changed.</summary>
    public void Purge(DateTimeOffset now)
    {
        foreach (var old in _run.Where(pair => now - pair.Value > _runLasts).Select(pair => pair.Key).ToList())
        {
            _run.Remove(old);
        }

        foreach (var old in _aside.Where(pair => now - pair.Value.At > _runLasts).Select(pair => pair.Key).ToList())
        {
            _aside.Remove(old);
        }
    }

    private void Trim()
    {
        foreach (var held in new[] { _marks, _aside })
        {
            foreach (var oldest in held.OrderBy(pair => pair.Value.At).Select(pair => pair.Key).Take(Math.Max(held.Count - _bound, 0)).ToList())
            {
                held.Remove(oldest);
                _run.Remove(oldest);
            }
        }
    }

    private sealed record Mark(DateTimeOffset At, string? Title, int Rank, string? Version);
}
