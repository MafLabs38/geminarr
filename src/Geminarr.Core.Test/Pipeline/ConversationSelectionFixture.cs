using Geminarr.Core.Pipeline;

namespace Geminarr.Core.Test.Pipeline;

public sealed class ConversationSelectionFixture
{
    private static readonly DateTimeOffset Now = new(2026, 11, 18, 9, 0, 0, TimeSpan.Zero);

    private static ConversationRecord Known(int? rank = 3, string? title = "T", int known = 2, int sent = 2) =>
        new("c1", ConversationLines.TitleHashOf(title), Now, Now, Now, rank, known, sent, null, 0, "web", ConversationStates.Synced, null);

    [Fact]
    public void should_read_a_conversation_that_is_new()
    {
        ConversationSelection.NeedsReading(new ConversationHeader("c1", "T", 5), null, waitingToBeSent: false).Should().BeTrue();
    }

    [Fact]
    public void should_read_the_most_recent_one_every_time()
    {
        ConversationSelection.NeedsReading(new ConversationHeader("c1", "T", 0), Known(rank: 0), waitingToBeSent: false).Should().BeTrue();
    }

    [Fact]
    public void should_read_a_conversation_that_moved_up_or_was_renamed()
    {
        ConversationSelection.NeedsReading(new ConversationHeader("c1", "T", 1), Known(rank: 3), waitingToBeSent: false).Should().BeTrue();
        ConversationSelection.NeedsReading(new ConversationHeader("c1", "Another", 3), Known(rank: 3), waitingToBeSent: false).Should().BeTrue();
    }

    [Fact]
    public void should_read_one_whose_turns_are_known_but_not_sent_unless_they_wait_to_be_sent()
    {
        ConversationSelection.NeedsReading(new ConversationHeader("c1", "T", 3), Known(known: 4, sent: 2), waitingToBeSent: false).Should().BeTrue();
        ConversationSelection.NeedsReading(new ConversationHeader("c1", "T", 3), Known(known: 4, sent: 2), waitingToBeSent: true).Should().BeFalse();
    }

    [Fact]
    public void should_leave_a_conversation_that_has_not_changed()
    {
        ConversationSelection.NeedsReading(new ConversationHeader("c1", "T", 3), Known(rank: 3), waitingToBeSent: false).Should().BeFalse();
        ConversationSelection.NeedsReading(new ConversationHeader("c1", "T", 4), Known(rank: 3), waitingToBeSent: false).Should().BeFalse("it moved down, which is not news");
        ConversationSelection.NeedsReading(new ConversationHeader("c1", "T", 4), Known(rank: null), waitingToBeSent: false).Should().BeFalse();
    }

    private static List<ConversationHeader> List(int count) => [.. Enumerable.Range(0, count).Select(rank => new ConversationHeader($"c{rank}", "T", rank))];

    [Fact]
    public void should_bring_the_older_conversations_that_were_never_seen_a_few_at_a_time_in_the_order_of_the_list()
    {
        var options = new ReaderOptions { MaxListed = 3, BackfillPerPass = 2 };

        var next = ConversationSelection.ToBackfill(List(10), options, _ => false, _ => false);

        next.Select(header => header.Id).Should().Equal("c3", "c4");
    }

    [Fact]
    public void should_leave_the_ones_that_a_pass_looks_at_the_ones_that_are_known_and_the_ones_that_wait_or_show_nothing()
    {
        var options = new ReaderOptions { MaxListed = 3, BackfillPerPass = 10 };

        var next = ConversationSelection.ToBackfill(List(8), options, id => id == "c3", header => header.Id == "c4");

        next.Select(header => header.Id).Should().Equal("c5", "c6", "c7");
    }

    [Fact]
    public void should_bring_nothing_when_the_history_is_switched_off()
    {
        ConversationSelection.ToBackfill(List(10), new ReaderOptions { MaxListed = 3, BackfillPerPass = 0 }, _ => false, _ => false).Should().BeEmpty();
    }

    [Fact]
    public void should_say_how_far_the_history_is_walked_and_that_it_is_done_only_when_the_list_ends_and_all_of_it_is_known()
    {
        var options = new ReaderOptions { BackfillPerPass = 10 };
        var known = new HashSet<string> { "c0", "c1" };

        var walking = BackfillProgress.Of(List(4), known.Contains, options, listComplete: true);
        known.UnionWith(["c2", "c3"]);
        var all = BackfillProgress.Of(List(4), known.Contains, options, listComplete: true);
        var cut = BackfillProgress.Of(List(4), known.Contains, options, listComplete: false);

        (walking.Listed, walking.Known, walking.Done).Should().Be((4, 2, false));
        all.Done.Should().BeTrue();
        cut.Done.Should().BeFalse("a list that was cut is not the whole history");
        BackfillProgress.Of([], _ => false, options, listComplete: true).Done.Should().BeFalse("nothing listed is not a history that was read");
        BackfillProgress.Of(List(1), _ => true, new ReaderOptions { BackfillPerPass = 0 }, listComplete: true).Enabled.Should().BeFalse();
    }
}
