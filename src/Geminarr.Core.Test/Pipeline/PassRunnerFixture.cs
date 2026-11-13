using Geminarr.Core.Configuration;
using Geminarr.Core.Connection;
using Geminarr.Core.Link;
using Geminarr.Core.Pipeline;
using Geminarr.Core.Test.Access;
using Geminarr.Core.Test.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Geminarr.Core.Test.Pipeline;

public sealed class PassRunnerFixture : IDisposable
{
    private readonly TestDatabase _workspace = new();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 13, 9, 0, 0, TimeSpan.Zero));
    private readonly FakeReader _reader = new();
    private readonly FakeSink _sink = new();
    private readonly ConversationStore _conversations;
    private readonly OutboxStore _outbox;
    private readonly RunStore _runs;
    private readonly ConnectionTracker _connection;
    private readonly DestinationStore _destination;
    private LinkService _link = null!;
    private readonly FakeKeys _keys = new() { Key = "mnm_key" };

    public PassRunnerFixture()
    {
        _conversations = new ConversationStore(_workspace.Database);
        _outbox = new OutboxStore(_workspace.Database);
        _runs = new RunStore(_workspace.Database);
        _destination = new DestinationStore(_workspace.Database);
        _connection = new ConnectionTracker(new ConnectionStore(_workspace.Database, _clock), Options.Of(new ReaderOptions { LoginFailures = 2 }), _clock);
    }

    public void Dispose() => _workspace.Dispose();

    private PassRunner Subject(bool withReader = true, ReaderOptions? options = null, string? publicUrl = null)
    {
        _link = new LinkService(Options.Of(new MnemarrOptions { Url = "https://mnemarr.homelab.example" }), _destination, _keys, new MnemarrClient(new FakeHttp(_ => FakeHttp.Status(System.Net.HttpStatusCode.NotFound))), _clock, NullLogger<LinkService>.Instance, _outbox);
        var settings = new PassSettings(Options.Of(options ?? new ReaderOptions()), Options.Of(new RetentionOptions()), Options.Of(new GeminarrOptions { PublicUrl = publicUrl }), new ReaderProvider(withReader ? _reader : null));
        return new PassRunner(_sink, new PassStores(_conversations, _outbox, _runs), _connection, _link, settings, _clock, NullLogger<PassRunner>.Instance);
    }

    private static string Session(string id) => ConversationLines.Session(id);

    [Fact]
    public async Task should_send_a_new_conversation_from_the_start_with_its_meta_line_and_remember_what_mnemarr_holds()
    {
        _reader.Say("c1", "About bread", "How do I feed a starter?", "Weekly.", "Thanks");

        var result = await Subject().RunAsync(TestContext.Current.CancellationToken);

        result.Should().Be(new PassResult(RunOutcomes.Ok, null, 1, 1, 3));
        var (session, from, lines) = _sink.Sent.Single();
        session.Should().Be(Session("c1"));
        from.Should().Be(0);
        lines.Should().HaveCount(4);
        lines[0].Should().Contain("\"meta\"").And.Contain("About bread");
        lines[1].Should().Contain("How do I feed a starter?");
        var record = _conversations.Get("c1")!;
        (record.TurnsKnown, record.TurnsSent, record.State).Should().Be((3, 3, ConversationStates.Synced));
        _outbox.PendingLines().Should().Be(0);
        _connection.Current().State.Should().Be(ConnectionState.Connected);
    }

    [Fact]
    public async Task should_send_only_what_is_new_from_the_line_after_the_last_one_and_nothing_when_nothing_changed()
    {
        _reader.Say("c1", "T", "a", "b");
        var subject = Subject();
        await subject.RunAsync(TestContext.Current.CancellationToken);

        await subject.RunAsync(TestContext.Current.CancellationToken);
        _sink.Sent.Should().ContainSingle("nothing changed: the conversation is looked at, as it heads the list, and nothing is sent");
        _reader.Opened.Should().Equal("c1", "c1");

        _reader.Say("c1", "T", "a", "b", "c", "d");
        _reader.Conversations["c1"] = ("T renamed", _reader.Conversations["c1"].Turns);
        var result = await subject.RunAsync(TestContext.Current.CancellationToken);

        result!.TurnsSent.Should().Be(2);
        var (_, from, lines) = _sink.Sent[1];
        from.Should().Be(3, "line 0 is the meta line, then two turns: the next line is 3");
        lines.Should().HaveCount(2);
        lines[0].Should().Contain("\"text\":\"c\"");
        _conversations.Get("c1")!.TurnsSent.Should().Be(4);
    }

    [Fact]
    public async Task should_open_a_conversation_whose_rank_improved_and_only_the_best_ranked_ones_up_to_the_limit()
    {
        _reader.Say("a", "A", "1");
        _reader.Say("b", "B", "1");
        _reader.Say("c", "C", "1");
        var subject = Subject(options: new ReaderOptions { MaxOpenPerPass = 2 });

        var first = await subject.RunAsync(TestContext.Current.CancellationToken);

        first!.Opened.Should().Be(2);
        _reader.Opened.Should().Equal("a", "b");

        var second = await subject.RunAsync(TestContext.Current.CancellationToken);

        second!.Opened.Should().Be(2, "the one that heads the list is looked at every pass, and the one that waited is the candidate now");
        _reader.Opened.Should().Equal("a", "b", "a", "c");
    }

    [Fact]
    public async Task should_go_back_to_where_mnemarr_is_when_it_holds_less_than_was_believed_sent()
    {
        _reader.Say("c1", "T", "a", "b", "c");
        var subject = Subject();
        await subject.RunAsync(TestContext.Current.CancellationToken);
        _sink.Held[Session("c1")] = 2;
        _sink.Sent.Clear();
        _clock.Advance(TimeSpan.FromHours(2));

        var result = await subject.RunAsync(TestContext.Current.CancellationToken);

        _conversations.Get("c1")!.TurnsSent.Should().Be(3, "what Mnemarr holds is the truth, and the missing turn went again in the same pass or the next");
        result!.Outcome.Should().Be(RunOutcomes.Ok);
        _sink.Held[Session("c1")].Should().Be(4);
    }

    [Fact]
    public async Task should_send_again_everything_when_mnemarr_has_lost_the_conversation()
    {
        _reader.Say("c1", "T", "a", "b");
        var subject = Subject();
        await subject.RunAsync(TestContext.Current.CancellationToken);
        _sink.Held.Clear();
        _sink.Sent.Clear();
        _clock.Advance(TimeSpan.FromHours(2));

        await subject.RunAsync(TestContext.Current.CancellationToken);
        await subject.RunAsync(TestContext.Current.CancellationToken);

        _sink.Sent.Should().ContainSingle();
        _sink.Sent[0].From.Should().Be(0);
        _sink.Sent[0].Lines.Should().HaveCount(3);
    }

    [Fact]
    public async Task should_ask_for_the_positions_at_the_first_pass_and_about_every_hour_only()
    {
        _reader.Say("c1", "T", "a");
        var subject = Subject();

        await subject.RunAsync(TestContext.Current.CancellationToken);
        await subject.RunAsync(TestContext.Current.CancellationToken);
        _clock.Advance(TimeSpan.FromMinutes(61));
        await subject.RunAsync(TestContext.Current.CancellationToken);

        _sink.PositionCalls.Should().Be(2);
    }

    [Fact]
    public async Task should_keep_what_it_could_not_send_and_send_it_at_the_next_pass()
    {
        _reader.Say("c1", "T", "a", "b");
        _sink.FailSend = new SinkException(SinkFailure.Unreachable, "Mnemarr did not answer.");
        var subject = Subject();

        var failed = await subject.RunAsync(TestContext.Current.CancellationToken);

        failed!.Outcome.Should().Be(RunOutcomes.Partial);
        failed.Reason.Should().Be(DestinationProblems.Unreachable);
        failed.TurnsSent.Should().Be(0);
        _outbox.PendingLines().Should().Be(3);
        _outbox.Waiting(10).Single().Attempts.Should().Be(1);
        _sink.Heartbeats.Should().BeEmpty("Mnemarr could not be reached, and a heartbeat would have waited as long for the same answer");
        _destination.Get()!.LastProblem.Should().Be(DestinationProblems.Unreachable);

        _sink.FailSend = null;
        var recovered = await subject.RunAsync(TestContext.Current.CancellationToken);

        recovered!.TurnsSent.Should().Be(2);
        _outbox.PendingLines().Should().Be(0);
        _destination.Get()!.LastProblem.Should().BeNull();
    }

    [Fact]
    public async Task should_stop_sending_when_mnemarr_refuses_the_key_and_say_so()
    {
        _reader.Say("c1", "T", "a");
        _sink.FailPositions = new SinkException(SinkFailure.Refused, "Mnemarr refused the key of this machine.");
        _sink.FailHeartbeat = new SinkException(SinkFailure.Refused, "Mnemarr refused the key of this machine.");

        var result = await Subject().RunAsync(TestContext.Current.CancellationToken);

        result!.Outcome.Should().Be(RunOutcomes.Partial);
        result.Reason.Should().Be(DestinationProblems.Refused);
        _sink.Sent.Should().BeEmpty();
        _outbox.PendingLines().Should().Be(2, "what was read is queued, not lost");
        _destination.Get()!.LastProblem.Should().Be(DestinationProblems.Refused);
    }

    [Fact]
    public async Task should_keep_a_batch_that_mnemarr_rejected_and_go_on_with_the_other_conversations()
    {
        _reader.Say("bad", "Bad", "a");
        _reader.Say("good", "Good", "b");
        _sink.FailSession = (Session("bad"), new SinkException(SinkFailure.Rejected, "Mnemarr said no (400)."));

        var result = await Subject().RunAsync(TestContext.Current.CancellationToken);

        result!.Outcome.Should().Be(RunOutcomes.Partial);
        result.Reason.Should().Be(DestinationProblems.Rejected);
        _sink.Sent.Select(sent => sent.Session).Should().Equal(Session("good"));
        var waiting = _outbox.Waiting(10).Single();
        (waiting.ConversationId, waiting.Attempts, waiting.LastError).Should().Be(("bad", 1, "Mnemarr said no (400)."));
    }

    [Fact]
    public async Task should_not_ask_mnemarr_anything_when_nothing_is_linked_and_still_run()
    {
        _reader.Say("c1", "T", "a");
        _sink.FailPositions = new SinkException(SinkFailure.NotLinked, "No Mnemarr is linked.");

        var result = await Subject().RunAsync(TestContext.Current.CancellationToken);

        result!.Outcome.Should().Be(RunOutcomes.Partial);
        result.Reason.Should().Be(DestinationProblems.NotLinked);
        _outbox.PendingLines().Should().Be(2);
        _sink.Heartbeats.Should().BeEmpty("there is nowhere to send a heartbeat either");
    }

    [Fact]
    public async Task should_say_not_connected_and_read_nothing_when_no_reader_is_configured()
    {
        var result = await Subject(withReader: false).RunAsync(TestContext.Current.CancellationToken);

        result.Should().Be(new PassResult(RunOutcomes.Skipped, ConnectionReasons.NotConnected, 0, 0, 0));
        _connection.Current().State.Should().Be(ConnectionState.NotConnected);
        var heartbeat = _sink.Heartbeats.Single();
        heartbeat.Error.Should().Be("Not connected.");
        heartbeat.Attention.Should().BeNull();
    }

    [Fact]
    public async Task should_ask_the_person_to_sign_in_again_once_the_session_has_been_gone_for_several_passes()
    {
        _reader.Say("c1", "T", "a");
        var subject = Subject(publicUrl: "https://geminarr.homelab.example");
        await subject.RunAsync(TestContext.Current.CancellationToken);
        _reader.Session = SessionCheck.SignedOut;

        var first = await subject.RunAsync(TestContext.Current.CancellationToken);
        first!.Reason.Should().Be("signed-out");
        _sink.Heartbeats.Last().Attention.Should().BeNull("one pass is not enough to ask");

        var second = await subject.RunAsync(TestContext.Current.CancellationToken);

        second!.Outcome.Should().Be(RunOutcomes.Skipped);
        second.Reason.Should().Be(ConnectionReasons.NeedsLogin);
        var heartbeat = _sink.Heartbeats.Last();
        heartbeat.Error.Should().Be("The session has expired.");
        heartbeat.Attention.Should().Be(new Attention("needs-login", "https://geminarr.homelab.example/"));

        _reader.Session = SessionCheck.SignedIn;
        await subject.RunAsync(TestContext.Current.CancellationToken);

        _sink.Heartbeats.Last().Attention.Should().BeNull("the next heartbeat without it ends the episode");
        _sink.Heartbeats.Last().Error.Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("http://geminarr.homelab.example")]
    public async Task should_not_give_an_address_to_answer_that_mnemarr_would_drop(string? publicUrl)
    {
        var subject = Subject(publicUrl: publicUrl);
        _reader.Session = SessionCheck.SignedOut;

        await subject.RunAsync(TestContext.Current.CancellationToken);
        await subject.RunAsync(TestContext.Current.CancellationToken);

        _sink.Heartbeats.Last().Attention.Should().Be(new Attention("needs-login", null));
    }

    [Fact]
    public async Task should_give_the_address_of_a_loopback_sandbox()
    {
        var subject = Subject(publicUrl: "http://localhost:8282");
        _reader.Session = SessionCheck.SignedOut;

        await subject.RunAsync(TestContext.Current.CancellationToken);
        await subject.RunAsync(TestContext.Current.CancellationToken);

        _sink.Heartbeats.Last().Attention!.Url.Should().Be("http://localhost:8282/");
    }

    [Theory]
    [InlineData(ConnectionReasons.ReaderBroken, "reader-broken", "The assistant's page is no longer what the reader knows.")]
    [InlineData(ConnectionReasons.RateLimited, null, "The assistant asked to slow down.")]
    [InlineData(ConnectionReasons.Unreachable, null, "The assistant could not be reached.")]
    [InlineData("something-else", null, "The last pass failed.")]
    public async Task should_say_what_went_wrong_in_one_sentence_and_ask_only_for_what_a_person_can_do(string reason, string? attention, string sentence)
    {
        _reader.FailWith = new ReaderException(reason, "details that stay here");

        var result = await Subject().RunAsync(TestContext.Current.CancellationToken);

        result!.Outcome.Should().Be(RunOutcomes.Failed);
        result.Reason.Should().Be(reason);
        var heartbeat = _sink.Heartbeats.Single();
        heartbeat.Error.Should().Be(sentence);
        heartbeat.Attention?.Code.Should().Be(attention);
        _connection.Current().State.Should().Be(ConnectionState.Degraded);
    }

    [Fact]
    public async Task should_count_a_turn_that_was_rewritten_and_never_send_it_again()
    {
        _reader.Say("c1", "T", "a", "b");
        var subject = Subject();
        await subject.RunAsync(TestContext.Current.CancellationToken);
        _reader.Say("c1", "T", "a", "b edited");
        _reader.Conversations["c1"] = ("T2", _reader.Conversations["c1"].Turns);
        _sink.Sent.Clear();

        var result = await subject.RunAsync(TestContext.Current.CancellationToken);

        result!.TurnsSent.Should().Be(0);
        _sink.Sent.Should().BeEmpty();
        _conversations.Get("c1")!.RewrittenTurns.Should().Be(1);
    }

    [Fact]
    public async Task should_cut_a_long_conversation_into_batches_that_go_in_order()
    {
        _reader.Conversations["long"] = ("Long", [.. Enumerable.Range(0, 1_100).Select(index => new Turn(index % 2 == 0 ? "user" : "assistant", null, $"turn {index}"))]);

        var result = await Subject().RunAsync(TestContext.Current.CancellationToken);

        result!.TurnsSent.Should().Be(1_100);
        _sink.Sent.Select(sent => (sent.From, sent.Lines.Count)).Should().Equal((0L, 500), (500L, 500), (1_000L, 101));
        _conversations.Get("long")!.TurnsSent.Should().Be(1_100);
    }

    [Fact]
    public async Task should_send_nothing_for_a_conversation_that_has_no_turn_yet()
    {
        _reader.Say("empty", "Empty");

        var result = await Subject().RunAsync(TestContext.Current.CancellationToken);

        result!.TurnsSent.Should().Be(0);
        _sink.Sent.Should().BeEmpty();
        _conversations.Get("empty")!.State.Should().Be(ConversationStates.Synced);
    }

    [Fact]
    public async Task should_write_the_run_with_its_counts_and_keep_it_for_the_retention()
    {
        _reader.Say("c1", "T", "a", "b");
        var subject = Subject();

        await subject.RunAsync(TestContext.Current.CancellationToken);

        var run = _runs.Recent(1).Single();
        (run.Kind, run.Outcome, run.Listed, run.Opened, run.TurnsSent).Should().Be((RunKinds.Pass, RunOutcomes.Ok, 1, 1, 2));
        run.EndedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task should_run_one_pass_at_a_time()
    {
        var gate = new TaskCompletionSource();
        var slow = new SlowReader(gate.Task);
        var settings = new PassSettings(Options.Of(new ReaderOptions()), Options.Of(new RetentionOptions()), Options.Of(new GeminarrOptions()), new ReaderProvider(slow));
        var link = new LinkService(Options.Of(new MnemarrOptions { Url = "https://mnemarr.homelab.example" }), _destination, _keys, new MnemarrClient(new FakeHttp(_ => FakeHttp.Status(System.Net.HttpStatusCode.NotFound))), _clock, NullLogger<LinkService>.Instance, _outbox);
        var subject = new PassRunner(_sink, new PassStores(_conversations, _outbox, _runs), _connection, link, settings, _clock, NullLogger<PassRunner>.Instance);

        var first = subject.RunAsync(TestContext.Current.CancellationToken);
        await slow.Started.Task;

        subject.IsRunning.Should().BeTrue();
        (await subject.RunAsync(TestContext.Current.CancellationToken)).Should().BeNull();

        gate.SetResult();
        (await first).Should().NotBeNull();
        subject.IsRunning.Should().BeFalse();
    }

    [Fact]
    public async Task should_end_a_pass_that_runs_out_of_time_as_partial_and_keep_what_was_queued()
    {
        var never = new TaskCompletionSource();
        var slow = new SlowReader(never.Task);
        var settings = new PassSettings(Options.Of(new ReaderOptions { PassTimeoutMinutes = 1 }), Options.Of(new RetentionOptions()), Options.Of(new GeminarrOptions()), new ReaderProvider(slow));
        var link = new LinkService(Options.Of(new MnemarrOptions { Url = "https://mnemarr.homelab.example" }), _destination, _keys, new MnemarrClient(new FakeHttp(_ => FakeHttp.Status(System.Net.HttpStatusCode.NotFound))), _clock, NullLogger<LinkService>.Instance, _outbox);
        var subject = new PassRunner(_sink, new PassStores(_conversations, _outbox, _runs), _connection, link, settings, _clock, NullLogger<PassRunner>.Instance);

        var run = subject.RunAsync(TestContext.Current.CancellationToken);
        await slow.Started.Task;
        _clock.Advance(TimeSpan.FromMinutes(2));
        var result = await run;

        result!.Outcome.Should().Be(RunOutcomes.Partial);
        result.Reason.Should().Be("pass-timeout");
        _runs.Recent(1).Single().Outcome.Should().Be(RunOutcomes.Partial);
        _sink.Heartbeats.Should().ContainSingle("a reader that hangs ends the reading, not the heartbeat: Mnemarr still hears from the machine");
        _connection.Current().State.Should().Be(ConnectionState.Degraded, "a reader that hangs is a reader that failed: the reading backs off");
    }

    [Fact]
    public async Task should_keep_how_a_rejected_batch_went_when_its_conversation_is_queued_again()
    {
        _reader.Say("bad", "Bad", "a");
        _sink.FailSession = (Session("bad"), new SinkException(SinkFailure.Rejected, "Mnemarr said no (400)."));
        var subject = Subject();
        await subject.RunAsync(TestContext.Current.CancellationToken);
        var first = _outbox.Waiting(10).Single();

        await subject.RunAsync(TestContext.Current.CancellationToken);

        var second = _outbox.Waiting(10).Single();
        second.Id.Should().Be(first.Id, "the same batch, not a new one made of the same lines");
        second.Attempts.Should().Be(2);
        second.CreatedAt.Should().Be(first.CreatedAt);
    }

    [Fact]
    public async Task should_say_a_pass_failed_when_something_it_did_not_foresee_happens_and_stay_up()
    {
        _reader.Say("c1", "T", "a");
        var subject = Subject();
        await subject.RunAsync(TestContext.Current.CancellationToken);
        using (var connection = _workspace.Database.Open())
        {
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE conversations SET first_seen_at = 'not a date';";
            command.ExecuteNonQuery();
        }

        _reader.Say("c1", "T2", "a", "b");
        var result = await subject.RunAsync(TestContext.Current.CancellationToken);

        result!.Outcome.Should().Be(RunOutcomes.Failed);
        result.Reason.Should().Be("internal-error");
        _runs.Recent(1).Single().Outcome.Should().Be(RunOutcomes.Failed);
    }

    [Fact]
    public async Task should_ask_mnemarr_again_for_its_positions_when_the_destination_changes()
    {
        _reader.Say("c1", "T", "a");
        var subject = Subject();
        await subject.RunAsync(TestContext.Current.CancellationToken);
        _sink.PositionCalls.Should().Be(1);

        _link.Unlink();
        await subject.RunAsync(TestContext.Current.CancellationToken);

        _sink.PositionCalls.Should().Be(2, "another Mnemarr: what the last one said is not the truth about it");
    }

    [Fact]
    public async Task should_send_what_waits_and_tell_mnemarr_it_is_alive_in_a_pass_that_does_not_read()
    {
        _reader.Say("c1", "T", "a");
        var subject = Subject();
        _sink.FailSend = new SinkException(SinkFailure.Rejected, "Mnemarr said no (400).");
        await subject.RunAsync(TestContext.Current.CancellationToken);
        _sink.FailSend = null;
        _reader.Opened.Clear();

        var result = await subject.RunAsync(TestContext.Current.CancellationToken, reading: false);

        _reader.Opened.Should().BeEmpty("a pass that does not read does not open anything");
        result!.Reason.Should().Be("backoff");
        _sink.Sent.Should().ContainSingle("what waited went");
        _sink.Heartbeats.Should().HaveCount(2, "and Mnemarr heard from the machine");
    }

    [Fact]
    public async Task should_not_resume_from_the_positions_of_the_mnemarr_that_was_left_when_the_new_one_cannot_be_asked()
    {
        _reader.Say("c1", "T", "a", "b");
        _sink.Held[Session("c1")] = 3;
        var subject = Subject();
        await subject.RunAsync(TestContext.Current.CancellationToken);
        _conversations.SetTurnsSent("c1", 0);
        _link.Unlink();
        _sink.FailPositions = new SinkException(SinkFailure.Unreachable, "Mnemarr did not answer.");
        _reader.Say("c2", "T", "x", "y");

        await subject.RunAsync(TestContext.Current.CancellationToken);

        _outbox.Waiting(10).Single(batch => batch.ConversationId == "c2").FromPosition.Should().Be(0, "nothing is known of this Mnemarr: the conversation is queued from the start");
    }

    [Fact]
    public async Task should_adopt_what_mnemarr_holds_when_it_holds_more_than_was_believed_and_not_send_it_again()
    {
        _reader.Say("c1", "T", "a", "b", "c");
        _sink.Held[Session("c1")] = 4;

        var result = await Subject().RunAsync(TestContext.Current.CancellationToken);

        result!.TurnsSent.Should().Be(0);
        _sink.Sent.Should().BeEmpty();
        _conversations.Get("c1")!.TurnsSent.Should().Be(3);
    }

    [Fact]
    public async Task should_send_the_batches_of_a_long_wait_by_pages_in_order()
    {
        for (var conversation = 0; conversation < 45; conversation++)
        {
            _outbox.Replace($"c{conversation:00}", [(0, [ConversationLines.Meta($"c{conversation:00}", "T", "web")])], _clock.GetUtcNow());
        }

        var result = await Subject(withReader: false).RunAsync(TestContext.Current.CancellationToken);

        _sink.Sent.Should().HaveCount(45);
        _sink.Sent.Select(sent => sent.Session).Should().Equal(Enumerable.Range(0, 45).Select(index => Session($"c{index:00}")));
        _outbox.PendingLines().Should().Be(0);
        result!.Outcome.Should().Be(RunOutcomes.Skipped);
    }

    [Fact]
    public async Task should_send_the_address_of_a_page_under_a_sub_path_of_a_proxy()
    {
        var subject = Subject(publicUrl: "https://homelab.example/geminarr");
        _reader.Session = SessionCheck.SignedOut;

        await subject.RunAsync(TestContext.Current.CancellationToken);
        await subject.RunAsync(TestContext.Current.CancellationToken);

        _sink.Heartbeats.Last().Attention!.Url.Should().Be("https://homelab.example/geminarr/");
    }

    private sealed class SlowReader : IConversationReader
    {
        private readonly Task _release;

        public SlowReader(Task release)
        {
            _release = release;
        }

        public string Source => ConversationSources.Web;

        public TaskCompletionSource Started { get; } = new();

        public async Task<SessionCheck> CheckSessionAsync(CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            await _release.WaitAsync(cancellationToken);
            return SessionCheck.SignedIn;
        }

        public Task<IReadOnlyList<ConversationHeader>> ListRecentAsync(int limit, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ConversationHeader>>([]);

        public Task<IReadOnlyList<Turn>> OpenAsync(string id, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<Turn>>([]);
    }
}
