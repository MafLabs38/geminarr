using System.Diagnostics;
using Geminarr.Core.Connection;
using Geminarr.Core.Link;
using Geminarr.Core.SystemInfo;
using Microsoft.Extensions.Logging;

namespace Geminarr.Core.Pipeline;

/// <summary>
/// One pass, the unit of work (SPEC §3.3): check the connection, list what is recent, open what changed, queue what is new for Mnemarr, send what waits, tell Mnemarr how it went, write
/// the run. Sending and the heartbeat happen even when reading failed: what an earlier pass queued still goes, and Mnemarr must hear that Geminarr is alive and what is wrong.
/// </summary>
public sealed class PassRunner
{
    /// <summary>The positions Mnemarr holds are asked for at the first pass and about every hour (SPEC §5.3).</summary>
    private static readonly TimeSpan PositionsRefresh = TimeSpan.FromHours(1);

    /// <summary>How many waiting batches are read at a time.</summary>
    private const int OutboxPageSize = 20;

    private readonly IConversationSink _sink;
    private readonly IConversationStore _conversations;
    private readonly IOutboxStore _outbox;
    private readonly IRunStore _runs;
    private readonly ConnectionTracker _connection;
    private readonly LinkService _link;
    private readonly PassSettings _settings;
    private readonly TimeProvider _clock;
    private readonly ILogger<PassRunner> _logger;
    private readonly SemaphoreSlim _running = new(1, 1);
    private DateTimeOffset? _positionsAt;
    private long _generation = -1;

    /// <summary>The link as it was when the pass began: a pass that was running while the machine was unlinked queues nothing for the Mnemarr that is gone.</summary>
    private long _passGeneration;
    private SinkPositions? _positions;

    public PassRunner(IConversationSink sink, PassStores stores, ConnectionTracker connection, LinkService link, PassSettings settings, TimeProvider clock, ILogger<PassRunner> logger)
    {
        _sink = sink;
        _conversations = stores.Conversations;
        _outbox = stores.Outbox;
        _runs = stores.Runs;
        _connection = connection;
        _link = link;
        _settings = settings;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>Whether a pass is running: a second one is not started alongside.</summary>
    public bool IsRunning => _running.CurrentCount == 0;

    /// <summary>Runs a pass, or returns null when one is already running.</summary>
    public async Task<PassResult?> RunAsync(CancellationToken cancellationToken, bool reading = true)
    {
        if (!await _running.WaitAsync(0, cancellationToken))
        {
            return null;
        }

        try
        {
            return await RunOnceAsync(cancellationToken, reading);
        }
        finally
        {
            _running.Release();
        }
    }

    private async Task<PassResult> RunOnceAsync(CancellationToken cancellationToken, bool reading)
    {
        var watch = Stopwatch.StartNew();
        var startedAt = _clock.GetUtcNow();
        _passGeneration = _link.Generation;
        // A heartbeat between two readings writes its row at the end, and only if it has something to say: no row flickers in the list, and a process that dies leaves nothing half done.
        long? runId = reading ? _runs.Start(RunKinds.Pass, startedAt) : null;
        var state = new PassState();
        string? unforeseen = null;
        try
        {
            await ReconcileAsync(state, cancellationToken);
            await ReadWithinItsTimeAsync(state, reading, cancellationToken);
            if (Unlinked())
            {
                // Unlinked while this pass was reading: what it queued is text for a Mnemarr that is gone.
                _outbox.Clear();
            }

            await SendAsync(state, cancellationToken);
            await SignalAsync(state, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // The application is stopping: what was queued stays queued, and the run says so.
            state.Fail(RunOutcomes.Partial, "stopped");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Something this version did not foresee (a value in the database that cannot be read, a batch that cannot be unpacked): the run says so, the next pass tries again, and the application stays up.
            unforeseen = ex.GetType().Name;
            state.Fail(RunOutcomes.Failed, "internal-error");
        }

        if (unforeseen is not null)
        {
            // The kind of error only: from the reader that reads something real, the message of an exception may hold what was read.
            _logger.LogError("A pass failed with an error that was not foreseen ({Type}).", unforeseen);
        }

        var result = new PassResult(state.Outcome, state.Reason, state.Listed, state.Opened, state.TurnsSent);
        if (runId is null)
        {
            if (!IsWorthARun(result))
            {
                return result;
            }

            runId = _runs.Start(RunKinds.Heartbeat, startedAt);
        }

        _runs.Finish(runId.Value, _clock.GetUtcNow(), new RunOutcome(result.Outcome, result.Reason, result.Listed, result.Opened, result.TurnsSent, watch.ElapsedMilliseconds));
        _runs.Prune(_clock.GetUtcNow() - TimeSpan.FromDays(_settings.RunsDays));
        _logger.LogInformation("A pass ended: {Outcome}; listed {Listed}, opened {Opened}, sent {Turns} turns.", result.Outcome, result.Listed, result.Opened, result.TurnsSent);
        return result;
    }

    /// <summary>A heartbeat is a run when it sent turns, or when its state is not the one of the last heartbeat (a change is written once, not every ten minutes: the health page says what lasts; a reading says its own).</summary>
    private bool IsWorthARun(PassResult result)
    {
        var last = _runs.Latest(RunKinds.Heartbeat);
        var lastOutcome = last?.Outcome ?? RunOutcomes.Ok;
        var lastReason = last?.Reason;
        return result.TurnsSent > 0 || !string.Equals(lastOutcome, result.Outcome, StringComparison.Ordinal) || !string.Equals(lastReason, result.Reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// Reading has a time of its own (the setting of the pass): a reader that hangs ends the reading, and what waits is still sent and Mnemarr still hears from the machine. The time follows the
    /// clock of the application, so that a test moves it.
    /// </summary>
    private async Task ReadWithinItsTimeAsync(PassState state, bool reading, CancellationToken cancellationToken)
    {
        if (!reading)
        {
            // A pass that does not read still sends what waits and tells Mnemarr it is alive: a reader that fails and waits longer, or a slow poll interval, must not make the machine look gone.
            // It is a heartbeat, not a reading: it leaves a run only when it sent something or went wrong.
            return;
        }

        using var limit = new CancellationTokenSource(TimeSpan.FromMinutes(_settings.Reading.PassTimeoutMinutes), _clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, limit.Token);
        try
        {
            await ReadAsync(state, linked.Token);
        }
        catch (OperationCanceledException) when (limit.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            // A reader that hangs is a reader that failed: the connection says so, and the reading backs off, instead of hanging for its whole time at every pass.
            _connection.Failed(ConnectionReasons.Unreachable);
            state.Fail(RunOutcomes.Partial, "pass-timeout");
        }
    }

    /// <summary>Steps 1 to 4: may we read, is the session good, what is new.</summary>
    private async Task ReadAsync(PassState state, CancellationToken cancellationToken)
    {
        if (_settings.Reader is null)
        {
            _connection.NotConnected();
            state.Fail(RunOutcomes.Skipped, ConnectionReasons.NotConnected);
            return;
        }

        try
        {
            if (await FromReader(() => _settings.Reader.CheckSessionAsync(cancellationToken)) == SessionCheck.SignedOut)
            {
                var record = _connection.SignedOut();
                state.Fail(RunOutcomes.Skipped, record.State == ConnectionState.NeedsLogin ? ConnectionReasons.NeedsLogin : "signed-out");
                return;
            }

            var headers = await FromReader(() => _settings.Reader.ListRecentAsync(_settings.Reading.MaxListed, cancellationToken));
            state.Listed = headers.Count;
            foreach (var header in Candidates(headers).Take(_settings.Reading.MaxOpenPerPass))
            {
                Queue(header, await FromReader(() => _settings.Reader.OpenAsync(header.Id, cancellationToken)));
                state.Opened++;
            }

            _connection.Connected();
        }
        catch (ReaderException ex)
        {
            _connection.Failed(ex.Reason);
            state.Fail(RunOutcomes.Failed, ex.Reason);
        }
    }

    /// <summary>
    /// What a reader throws is the reader's failure, whatever it is: a reader says why with a <see cref="ReaderException"/>, and any other error of a reader (a file, the network, a page it did not expect) is a page it cannot read,
    /// so that the reading backs off instead of trying the assistant again at every wake. Only the stopping of the application passes through.
    /// </summary>
    private async Task<T> FromReader<T>(Func<Task<T>> call)
    {
        string failure;
        try
        {
            return await call();
        }
        catch (Exception ex) when (ex is not (OperationCanceledException or ReaderException or OutOfMemoryException))
        {
            failure = ex.GetType().Name;
        }

        // The kind of error only: the message of an exception of a reader that reads something real may hold what was read.
        _logger.LogWarning("A reader failed with an error that is not its own ({Type}).", failure);
        throw new ReaderException(ConnectionReasons.ReaderBroken, "The reader failed.");
    }

    /// <summary>A conversation is a candidate when it is new, moved up the list, was renamed, or has turns that nothing is waiting to send (SPEC §6.2); best rank first.</summary>
    private IEnumerable<ConversationHeader> Candidates(IReadOnlyList<ConversationHeader> headers)
    {
        var waiting = _outbox.ConversationsWaiting();
        return headers.Where(header =>
        {
            // Nothing is opened until Mnemarr has been asked what it holds (and again after an unlink): a conversation goes on from where Mnemarr stands, and no text waits with nowhere to go.
            if (_positions is null)
            {
                return false;
            }

            var known = _conversations.Get(header.Id);
            if (known is null)
            {
                return true;
            }

            // The most recent conversation is looked at every pass: new turns do not move it up a list that it already heads.
            return header.Rank == 0
                || header.Rank < known.ListRank
                || !string.Equals(ConversationLines.TitleHashOf(header.Title), known.TitleHash, StringComparison.Ordinal)
                || (known.TurnsKnown > known.TurnsSent && !waiting.Contains(header.Id));
        }).OrderBy(header => header.Rank);
    }

    /// <summary>Queues the lines of a conversation that Mnemarr does not hold yet, from where Mnemarr stands.</summary>
    private void Queue(ConversationHeader header, IReadOnlyList<Turn> turns)
    {
        if (Unlinked())
        {
            return;
        }

        var now = _clock.GetUtcNow();
        var known = _conversations.Get(header.Id);
        // A conversation that was never seen here but that Mnemarr already holds (this database was lost, or another Geminarr sent it): it goes on from where Mnemarr stands.
        var sent = Math.Min(known?.TurnsSent ?? HeldTurns(header.Id), turns.Count);
        var hash = turns.Count > 0 ? ConversationLines.HashOf(turns[^1]) : null;
        // The last turn that was known is not what it was (an edit, a regenerated answer): it is counted and shown, never sent again, because the position model cannot update a line.
        var rewritten = (known?.RewrittenTurns ?? 0) + (IsRewritten(known, turns) ? 1 : 0);

        var lines = new List<string>();
        if (sent == 0)
        {
            lines.Add(ConversationLines.Meta(header.Id, header.Title, _settings.Reader!.Source));
        }

        lines.AddRange(turns.Skip(sent).Select(ConversationLines.TurnLine));
        var from = sent == 0 ? 0 : sent + 1L;
        var nothingNew = lines.Count == (sent == 0 ? 1 : 0);
        _outbox.Replace(header.Id, nothingNew ? [] : ConversationLines.Batches(from, lines), now);
        _conversations.Save(new ConversationRecord(
            header.Id,
            ConversationLines.TitleHashOf(header.Title),
            known?.FirstSeenAt ?? now,
            now,
            now,
            header.Rank,
            turns.Count,
            sent,
            hash,
            rewritten,
            _settings.Reader!.Source,
            turns.Count == sent ? ConversationStates.Synced : ConversationStates.New,
            null));
    }

    private static bool IsRewritten(ConversationRecord? known, IReadOnlyList<Turn> turns) =>
        known is { TurnsKnown: > 0, LastTurnHash: { } before } && turns.Count >= known.TurnsKnown && ConversationLines.HashOf(turns[known.TurnsKnown - 1]) != before;

    /// <summary>The link changed during this pass and there is no destination now (an unlink); a pairing that completes with the same Mnemarr changes it too, and loses nothing.</summary>
    private bool Unlinked() => _link.Generation != _passGeneration && !_link.Status().Linked;

    /// <summary>Step 5: send what waits, in order, unless Mnemarr could not be reached before.</summary>
    private async Task SendAsync(PassState state, CancellationToken cancellationToken)
    {
        if (state.Destination is not null)
        {
            return;
        }

        try
        {
            await FlushAsync(state, cancellationToken);
        }
        catch (SinkException ex)
        {
            state.Destination = ex.Failure;
            state.Fail(RunOutcomes.Partial, ProblemOf(ex.Failure));
        }
    }

    /// <summary>Step 1: Mnemarr's positions are the truth about what it holds; they are asked for at the first pass and about every hour, before anything is read, so that what is queued starts from where it stands.</summary>
    private async Task ReconcileAsync(PassState state, CancellationToken cancellationToken)
    {
        // Another Mnemarr (unlinked, another address, another machine name): what was asked of the last one is not the truth about this one.
        if (_generation != _link.Generation)
        {
            _generation = _link.Generation;
            _positionsAt = null;
            _positions = null;
        }

        if (_positionsAt is { } at && _clock.GetUtcNow() - at < PositionsRefresh)
        {
            return;
        }

        try
        {
            _positions = await _sink.PositionsAsync(cancellationToken);
            ApplyPositions(_positions);
            _positionsAt = _clock.GetUtcNow();
        }
        catch (SinkException ex)
        {
            state.Destination = ex.Failure;
            state.Fail(RunOutcomes.Partial, ProblemOf(ex.Failure));
        }
    }

    /// <summary>Turns that a position stands for: line 0 is the meta line. What Mnemarr says is checked like anything that comes from outside: a count that does not fit is clamped.</summary>
    private static int TurnsHeld(long lines) => (int)Math.Clamp(lines - 1, 0, int.MaxValue);

    private int HeldTurns(string conversationId) =>
        _positions is { } positions && positions.Lines.TryGetValue(ConversationLines.Session(conversationId), out var lines) ? TurnsHeld(lines) : 0;

    private void ApplyPositions(SinkPositions positions)
    {
        foreach (var conversation in _conversations.Recent(int.MaxValue))
        {
            var held = positions.Lines.TryGetValue(ConversationLines.Session(conversation.Id), out var lines) ? TurnsHeld(lines) : 0;
            if (held == conversation.TurnsSent)
            {
                continue;
            }

            _conversations.SetTurnsSent(conversation.Id, held);
            // What waits was cut from another place: it is made again from where Mnemarr stands, at the next opening of the conversation.
            _outbox.Replace(conversation.Id, [], _clock.GetUtcNow());
        }
    }

    private async Task FlushAsync(PassState state, CancellationToken cancellationToken)
    {
        var skipped = new HashSet<string>(StringComparer.Ordinal);
        var after = 0L;
        // By pages: a long wait (Mnemarr down for days while the reader backfills) must not put every waiting turn in memory at once.
        for (var page = _outbox.Waiting(OutboxPageSize, after); page.Count > 0; page = _outbox.Waiting(OutboxPageSize, after))
        {
            foreach (var batch in page)
            {
                after = batch.Id;
                if (!skipped.Contains(batch.ConversationId))
                {
                    await SendBatchAsync(batch, skipped, state, cancellationToken);
                }
            }
        }
    }

    private async Task SendBatchAsync(OutboxBatch batch, HashSet<string> skipped, PassState state, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _sink.SendAsync(ConversationLines.Session(batch.ConversationId), batch.FromPosition, batch.Lines, cancellationToken);
            Acknowledge(batch, result, state);
        }
        catch (SinkException ex) when (ex.Failure == SinkFailure.Rejected)
        {
            // Mnemarr said no to this batch: it is kept with the reason, the rest of its conversation waits behind it, and the other conversations go on.
            _outbox.RecordFailure(batch.Id, ex.Message);
            skipped.Add(batch.ConversationId);
            state.Destination = ex.Failure;
            state.Fail(RunOutcomes.Partial, ProblemOf(ex.Failure));
        }
        catch (SinkException ex)
        {
            _outbox.RecordFailure(batch.Id, ex.Message);
            throw;
        }
    }

    private void Acknowledge(OutboxBatch batch, SinkResult result, PassState state)
    {
        _outbox.Delete(batch.Id);
        var turnsHeld = TurnsHeld(result.Next);
        _conversations.SetTurnsSent(batch.ConversationId, turnsHeld);
        if (_conversations.Get(batch.ConversationId) is { } conversation && conversation.TurnsKnown == turnsHeld && conversation.State != ConversationStates.Synced)
        {
            _conversations.Save(conversation with { State = ConversationStates.Synced });
        }

        state.TurnsSent += Math.Max(0, result.Accepted - (batch.FromPosition == 0 ? 1 : 0));
    }

    /// <summary>Step 7: the sign of life, with what is wrong and what a person is asked to do; the outcome of the talk is kept for the page and the health.</summary>
    private async Task SignalAsync(PassState state, CancellationToken cancellationToken)
    {
        var connection = _connection.Current();
        // Mnemarr could not be reached, or refused the key, or there is none: a heartbeat would fail the same way, and a call that fails takes up to its time limit.
        if (state.Destination is not (null or SinkFailure.Rejected))
        {
            RecordTalk(state);
            return;
        }

        try
        {
            await _sink.HeartbeatAsync(new Heartbeat(BuildInfo.Version, _outbox.PendingLines(), ErrorOf(connection), AttentionOf(connection)), cancellationToken);
        }
        catch (SinkException ex)
        {
            state.Destination = ex.Failure;
            state.Fail(RunOutcomes.Partial, ProblemOf(ex.Failure));
        }

        RecordTalk(state);
    }

    private void RecordTalk(PassState state)
    {
        if (state.Destination != SinkFailure.NotLinked && state.Destination != SinkFailure.InsecureAddress)
        {
            _link.RecordTalk(state.Destination is { } failure ? ProblemOf(failure) : null);
        }
    }

    private static string ProblemOf(SinkFailure failure) => failure switch
    {
        SinkFailure.NotLinked => DestinationProblems.NotLinked,
        SinkFailure.InsecureAddress => DestinationProblems.InsecureAddress,
        SinkFailure.Refused => DestinationProblems.Refused,
        SinkFailure.Rejected => DestinationProblems.Rejected,
        _ => DestinationProblems.Unreachable,
    };

    /// <summary>A short sentence with no path and no content: what Mnemarr shows next to the machine.</summary>
    private static string? ErrorOf(ConnectionRecord connection) => connection.State switch
    {
        ConnectionState.Connected => null,
        ConnectionState.NotConnected => "Not connected.",
        ConnectionState.NeedsLogin => "The session has expired.",
        _ => connection.LastReason switch
        {
            ConnectionReasons.ReaderBroken => "The assistant's page is no longer what the reader knows.",
            ConnectionReasons.RateLimited => "The assistant asked to slow down.",
            ConnectionReasons.Unreachable => "The assistant could not be reached.",
            _ => "The last pass failed.",
        },
    };

    /// <summary>What a person is asked for: only the two codes Mnemarr knows. The address is where to answer, given only when Mnemarr would keep it (https, or loopback).</summary>
    private Attention? AttentionOf(ConnectionRecord connection)
    {
        string? code = null;
        if (connection.State == ConnectionState.NeedsLogin)
        {
            code = ConnectionReasons.NeedsLogin;
        }
        else if (connection.LastReason == ConnectionReasons.ReaderBroken)
        {
            code = ConnectionReasons.ReaderBroken;
        }

        if (code is null)
        {
            return null;
        }

        return new Attention(code, MnemarrAddress.AttentionLink(_settings.PublicUrl));
    }

    private sealed class PassState
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
}
