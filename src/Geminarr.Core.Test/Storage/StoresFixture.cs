using Geminarr.Core.Connection;
using Geminarr.Core.Link;
using Geminarr.Core.Pipeline;
using Geminarr.Core.Test.Support;
using Microsoft.Extensions.Time.Testing;

namespace Geminarr.Core.Test.Storage;

/// <summary>The stores, against a real database file.</summary>
public sealed class StoresFixture : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 13, 9, 30, 0, TimeSpan.Zero);

    private readonly TestDatabase _workspace = new();
    private readonly FakeTimeProvider _clock = new(Now);

    public void Dispose() => _workspace.Dispose();

    private static ConversationRecord Conversation(string id, int known = 0, int sent = 0) =>
        new(id, "a1b2c3d4e5f60718", Now, Now, null, 3, known, sent, null, 0, ConversationSources.Web, ConversationStates.New, null);

    [Fact]
    public void should_leave_nothing_of_an_acknowledged_batch_in_the_write_ahead_log()
    {
        var store = new OutboxStore(_workspace.Database);
        store.Replace("c1", ConversationLines.Batches(0, [.. Enumerable.Range(0, 200).Select(index => $"line {index} " + new string('x', 400))]), Now);
        var wal = Path.Combine(_workspace.Folder, "geminarr.db-wal");

        store.Delete(store.Waiting(10).Single().Id);

        (File.Exists(wal) ? new FileInfo(wal).Length : 0).Should().Be(0, "a deleted batch is not kept in the log either");
    }

    [Fact]
    public void should_keep_a_conversation_and_update_it_without_losing_when_it_was_first_seen()
    {
        var store = new ConversationStore(_workspace.Database);
        store.Save(Conversation("c1"));

        store.Save(Conversation("c1", known: 4, sent: 2) with { FirstSeenAt = Now.AddDays(1), LastSeenAt = Now.AddHours(1), LastOpenedAt = Now, LastTurnHash = "h", RewrittenTurns = 1, LastError = "e" });

        var saved = store.Get("c1")!;
        saved.FirstSeenAt.Should().Be(Now, "the first sighting is not rewritten");
        saved.TurnsKnown.Should().Be(4);
        saved.TurnsSent.Should().Be(2);
        saved.LastTurnHash.Should().Be("h");
        saved.LastOpenedAt.Should().Be(Now);
        saved.LastError.Should().Be("e");
        store.Get("missing").Should().BeNull();
        store.Count().Should().Be(1);
    }

    [Fact]
    public void should_list_the_conversations_most_recently_seen_first_and_set_what_mnemarr_holds()
    {
        var store = new ConversationStore(_workspace.Database);
        store.Save(Conversation("old") with { LastSeenAt = Now });
        store.Save(Conversation("new") with { LastSeenAt = Now.AddHours(1) });

        store.Recent(10).Select(conversation => conversation.Id).Should().Equal("new", "old");
        store.Recent(1).Should().ContainSingle();

        store.SetTurnsSent("old", 7);
        store.Get("old")!.TurnsSent.Should().Be(7);
    }

    [Fact]
    public void should_hold_batches_in_order_and_give_back_the_lines_as_they_were()
    {
        var store = new OutboxStore(_workspace.Database);
        string[] lines = ["{\"v\":1,\"meta\":{}}", "{\"v\":1,\"role\":\"user\",\"text\":\"héllo \\\"quoted\\\" é\"}"];

        store.Replace("c1", [(0, lines)], Now);
        store.Replace("c2", [(5, ["x"]), (6, ["y", "z"])], Now.AddMinutes(1));

        var waiting = store.Waiting(10);
        waiting.Select(batch => (batch.ConversationId, batch.FromPosition)).Should().Equal(("c1", 0L), ("c2", 5L), ("c2", 6L));
        waiting[0].Lines.Should().Equal(lines);
        store.PendingLines().Should().Be(5);
        store.OldestAt().Should().Be(Now);
        store.ConversationsWaiting().Should().BeEquivalentTo(["c1", "c2"]);
        store.Waiting(1).Should().ContainSingle();
    }

    [Fact]
    public void should_replace_what_waits_for_a_conversation_and_leave_the_others()
    {
        var store = new OutboxStore(_workspace.Database);
        store.Replace("c1", [(0, ["a"])], Now);
        store.Replace("c2", [(0, ["b"])], Now);

        store.Replace("c1", [], Now);

        store.ConversationsWaiting().Should().BeEquivalentTo(["c2"]);
        store.OldestAt().Should().Be(Now);
    }

    [Fact]
    public void should_keep_a_batch_that_is_wanted_again_as_it_was_and_replace_one_that_changed()
    {
        var store = new OutboxStore(_workspace.Database);
        store.Replace("c1", [(0, ["a", "b"]), (2, ["c"])], Now);
        var before = store.Waiting(10);
        store.RecordFailure(before[0].Id, "Mnemarr said no (400).");

        store.Replace("c1", [(0, ["a", "b"]), (2, ["c", "d"])], Now.AddMinutes(5));

        var after = store.Waiting(10);
        after[0].Should().Match<OutboxBatch>(batch => batch.Id == before[0].Id && batch.Attempts == 1 && batch.CreatedAt == Now);
        after[1].Lines.Should().Equal("c", "d");
        store.Waiting(10).Should().HaveCount(2);
    }

    [Fact]
    public void should_forget_a_batch_that_was_acknowledged_and_count_the_failures_of_one_that_was_not()
    {
        var store = new OutboxStore(_workspace.Database);
        store.Replace("c1", [(0, ["a"]), (1, ["b"])], Now);
        var first = store.Waiting(10)[0];

        store.RecordFailure(first.Id, "Mnemarr said no (400).");
        store.RecordFailure(first.Id, "Mnemarr said no (400).");
        store.Delete(store.Waiting(10)[1].Id);

        var remaining = store.Waiting(10).Should().ContainSingle().Subject;
        remaining.Attempts.Should().Be(2);
        remaining.LastError.Should().Be("Mnemarr said no (400).");
        new OutboxStore(_workspace.Database).OldestAt().Should().Be(Now);
    }

    [Fact]
    public void should_say_nothing_waits_when_nothing_does()
    {
        var store = new OutboxStore(_workspace.Database);

        store.PendingLines().Should().Be(0);
        store.OldestAt().Should().BeNull();
        store.Waiting(10).Should().BeEmpty();
    }

    [Fact]
    public void should_record_a_run_from_its_start_to_its_end_and_forget_the_old_ones()
    {
        var store = new RunStore(_workspace.Database);
        var first = store.Start(RunKinds.Pass, Now.AddDays(-100));
        store.Finish(first, Now.AddDays(-100), new RunOutcome(RunOutcomes.Ok, null, 1, 1, 1, 10));
        var second = store.Start(RunKinds.Pass, Now);

        store.Recent(10).Select(run => run.Id).Should().Equal(second, first);
        store.Recent(10)[0].Outcome.Should().BeNull("it has not ended");

        store.Finish(second, Now.AddSeconds(2), new RunOutcome(RunOutcomes.Partial, "mnemarr-unreachable", 5, 3, 8, 2000));
        var ended = store.Recent(1).Single();
        (ended.Outcome, ended.Reason, ended.Listed, ended.Opened, ended.TurnsSent, ended.DurationMs).Should().Be((RunOutcomes.Partial, "mnemarr-unreachable", 5, 3, 8, 2000L));

        store.Prune(Now.AddDays(-90));
        store.Recent(10).Should().ContainSingle().Which.Id.Should().Be(second);
    }

    [Fact]
    public void should_say_the_connection_is_not_made_until_a_state_is_recorded()
    {
        var store = new ConnectionStore(_workspace.Database, _clock);

        var initial = store.Get();
        initial.State.Should().Be(ConnectionState.NotConnected);
        initial.Since.Should().Be(Now);

        store.Save(new ConnectionRecord(ConnectionState.Degraded, Now, Now.AddMinutes(-5), 2, ConnectionReasons.Unreachable));

        store.Get().Should().Be(new ConnectionRecord(ConnectionState.Degraded, Now, Now.AddMinutes(-5), 2, ConnectionReasons.Unreachable));
    }

    [Fact]
    public void should_keep_the_destination_and_forget_the_link_when_the_address_changes()
    {
        var store = new DestinationStore(_workspace.Database);
        store.Get().Should().BeNull();

        store.SaveAddress("https://mnemarr.homelab.example");
        store.SaveLinked("gemini-2", Now);
        store.RecordHeartbeat(Now.AddMinutes(1), DestinationProblems.Unreachable);

        store.Get().Should().Be(new DestinationRecord("https://mnemarr.homelab.example", "gemini-2", Now, Now.AddMinutes(1), DestinationProblems.Unreachable));

        store.EnsureAddress("https://mnemarr.homelab.example");
        store.Get()!.Machine.Should().Be("gemini-2", "the same address changes nothing");

        store.SaveAddress("https://other.homelab.example");
        store.Get().Should().Be(new DestinationRecord("https://other.homelab.example", null, null, null, null));

        store.Clear();
        store.Get().Should().BeNull();
    }
}
