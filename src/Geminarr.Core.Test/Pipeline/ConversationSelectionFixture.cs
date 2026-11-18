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
}
