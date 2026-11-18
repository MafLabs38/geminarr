using Geminarr.Core.Extension;
using Geminarr.Core.Pipeline;

namespace Geminarr.Core.Test.Extension;

public sealed class ExtensionInboxFixture
{
    private static readonly DateTimeOffset Now = new(2026, 11, 18, 9, 0, 0, TimeSpan.Zero);
    private readonly ExtensionInbox _subject = new();

    [Fact]
    public void should_know_nothing_before_the_extension_has_said_something()
    {
        _subject.Session.Should().BeNull();
        _subject.List().Should().BeEmpty();
        _subject.Has("c1").Should().BeFalse();
        _subject.Take("c1").Should().BeNull();
    }

    [Fact]
    public void should_keep_the_last_word_about_the_session()
    {
        _subject.RecordSession(signedIn: true, "1.0.0", Now);
        _subject.RecordSession(signedIn: false, "1.0.1", Now.AddMinutes(5));

        _subject.Session.Should().Be(new ExtensionSession(false, "1.0.1", Now.AddMinutes(5)));
    }

    [Fact]
    public void should_take_a_list_and_a_conversation_for_a_word_about_the_session_too()
    {
        _subject.RecordList([new ConversationHeader("c1", "T", 0)], null, Now);
        _subject.Session!.At.Should().Be(Now);

        _subject.RecordSession(signedIn: false, null, Now.AddMinutes(1));
        _subject.RecordConversation("c1", "T", [new Turn("user", null, "hi")], Now.AddMinutes(2));

        _subject.Session!.At.Should().Be(Now.AddMinutes(2));
        _subject.Session.SignedIn.Should().BeFalse("a conversation is not a word about who is signed in: the state keeps what it was");
    }

    [Fact]
    public void should_hand_a_conversation_over_once_and_keep_no_copy()
    {
        _subject.RecordConversation("c1", "T", [new Turn("user", null, "hi"), new Turn("assistant", null, "hello")], Now);

        _subject.Has("c1").Should().BeTrue();
        _subject.Take("c1")!.Select(turn => turn.Text).Should().Equal("hi", "hello");
        _subject.Has("c1").Should().BeFalse("what was taken is no longer here: Geminarr keeps no second copy of a conversation");
        _subject.Take("c1").Should().BeNull();
    }

    [Fact]
    public void should_hold_a_bounded_number_of_conversations_and_drop_the_oldest_first()
    {
        for (var i = 0; i < ExtensionInbox.MaxConversations + 5; i++)
        {
            _subject.RecordConversation($"c{i}", null, [new Turn("user", null, "x")], Now.AddSeconds(i));
        }

        _subject.Has("c0").Should().BeFalse();
        _subject.Has("c4").Should().BeFalse();
        _subject.Has("c5").Should().BeTrue();
        _subject.Has($"c{ExtensionInbox.MaxConversations + 4}").Should().BeTrue();
    }

    [Fact]
    public void should_replace_a_conversation_that_is_brought_again()
    {
        _subject.RecordConversation("c1", "T", [new Turn("user", null, "first")], Now);
        _subject.RecordConversation("c1", "T", [new Turn("user", null, "first"), new Turn("assistant", null, "second")], Now.AddMinutes(1));

        _subject.Take("c1")!.Should().HaveCount(2);
    }

    [Fact]
    public void should_hold_a_bounded_amount_of_text_and_drop_the_oldest_first_but_never_the_one_that_just_came()
    {
        var subject = new ExtensionInbox(maxHeldCharacters: 100);
        var heavy = new string('x', 51);
        subject.RecordConversation("first", "T", [new Turn("user", null, heavy)], Now);
        subject.RecordConversation("second", "T", [new Turn("user", null, heavy)], Now.AddSeconds(1));
        subject.RecordConversation("third", "T", [new Turn("user", null, "short")], Now.AddSeconds(2));

        subject.Has("first").Should().BeFalse("it was the oldest, and the two heavy ones were over the bound");
        subject.Has("second").Should().BeTrue();
        subject.Has("third").Should().BeTrue();
    }

    [Fact]
    public void should_keep_the_conversation_that_just_came_even_when_it_alone_is_over_the_bound()
    {
        var subject = new ExtensionInbox(maxHeldCharacters: 10);

        subject.RecordConversation("big", "T", [new Turn("user", null, new string('x', 50))], Now);

        subject.Has("big").Should().BeTrue();
    }

    [Fact]
    public void should_say_that_a_list_is_news_after_a_page_that_nobody_was_signed_in_to_or_that_was_broken_and_not_otherwise()
    {
        _subject.RecordList([], null, Now).Should().BeFalse("the first word");
        _subject.RecordList([], null, Now).Should().BeFalse("signed in already");

        _subject.RecordSession(signedIn: false, null, Now);
        _subject.RecordList([], null, Now).Should().BeTrue();

        _subject.RecordBroken(ExtensionBrokenSteps.List, null, Now);
        _subject.RecordList([], null, Now).Should().BeTrue();
        _subject.Session!.Broken.Should().BeNull("a list that was read is a page that is known");
    }

    [Fact]
    public void should_keep_a_broken_page_with_its_step_until_something_is_read_well_again()
    {
        _subject.RecordSession(signedIn: true, "1.0.0", Now);

        _subject.RecordBroken(ExtensionBrokenSteps.Turns, "1.0.0", Now.AddMinutes(1));
        _subject.Session.Should().Be(new ExtensionSession(true, "1.0.0", Now.AddMinutes(1), ExtensionBrokenSteps.Turns));

        _subject.RecordConversation("c1", "T", [new Turn("user", null, "hi")], Now.AddMinutes(2));
        _subject.Session!.Broken.Should().BeNull();

        _subject.RecordBroken(ExtensionBrokenSteps.Page, null, Now.AddMinutes(3));
        _subject.RecordSession(signedIn: false, null, Now.AddMinutes(4));
        _subject.Session!.Broken.Should().BeNull();
    }

    [Theory]
    [InlineData("page", true)]
    [InlineData("list", true)]
    [InlineData("turns", true)]
    [InlineData("Page", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("a sentence of the page", false)]
    public void should_know_three_steps_and_nothing_a_page_could_make_up(string? step, bool known)
    {
        ExtensionBrokenSteps.IsKnown(step).Should().Be(known);
    }

    [Fact]
    public void should_not_take_one_or_two_conversations_without_a_turn_for_a_page_that_changed_but_three_in_a_row()
    {
        _subject.RecordSession(signedIn: true, null, Now);

        _subject.RecordUnreadable("c1", null, Now).Should().BeFalse();
        _subject.RecordUnreadable("c2", null, Now).Should().BeFalse();
        _subject.Session!.Broken.Should().BeNull();
        _subject.RecordUnreadable("c3", "1.0.0", Now).Should().BeTrue();
        _subject.Session.Should().Be(new ExtensionSession(true, "1.0.0", Now, ExtensionBrokenSteps.Turns));
    }

    [Fact]
    public void should_forget_the_unreadable_ones_when_a_conversation_is_read_and_after_an_hour_and_when_nobody_is_signed_in()
    {
        _subject.RecordUnreadable("c1", null, Now);
        _subject.RecordUnreadable("c2", null, Now);
        _subject.RecordConversation("c9", "T", [new Turn("user", null, "hi")], Now);
        _subject.IsUnreadable("c1").Should().BeFalse("a conversation was read: the page is the one that is known");

        _subject.RecordUnreadable("c3", null, Now);
        _subject.RecordUnreadable("c4", null, Now.Add(ExtensionInbox.HeldFor).AddMinutes(1));
        _subject.IsUnreadable("c3").Should().BeFalse("an hour passed");
        _subject.IsUnreadable("c4").Should().BeTrue();

        _subject.RecordSession(signedIn: false, null, Now);
        _subject.IsUnreadable("c4").Should().BeFalse();
    }

    [Fact]
    public void should_not_ask_about_more_conversations_than_it_holds()
    {
        _subject.Ask(Enumerable.Range(0, ExtensionInbox.MaxConversations + 5).Select(i => $"c{i}"), Now);

        _subject.WasAsked("c0").Should().BeFalse("the oldest questions go first, whatever the order they were put in");
        _subject.WasAsked($"c{ExtensionInbox.MaxConversations + 4}").Should().BeTrue();
    }

    [Fact]
    public void should_not_mend_the_turns_with_a_list_but_mend_the_page_and_the_list_with_one()
    {
        _subject.RecordBroken(ExtensionBrokenSteps.Turns, null, Now);
        _subject.RecordList([], null, Now).Should().BeFalse("a list is no news about the turns");
        _subject.Session!.Broken.Should().Be(ExtensionBrokenSteps.Turns);

        _subject.RecordConversation("c1", "T", [new Turn("user", null, "hi")], Now);
        _subject.Session!.Broken.Should().BeNull("a conversation that was read mends them");

        _subject.RecordBroken(ExtensionBrokenSteps.List, null, Now);
        _subject.RecordList([], null, Now).Should().BeTrue();
        _subject.Session!.Broken.Should().BeNull();
    }

    [Fact]
    public void should_hold_as_many_unreadable_ones_as_conversations_and_no_more()
    {
        for (var i = 0; i < ExtensionInbox.MaxConversations + 5; i++)
        {
            _subject.RecordUnreadable($"c{i}", null, Now.AddSeconds(i));
        }

        _subject.IsUnreadable("c0").Should().BeFalse();
        _subject.IsUnreadable($"c{ExtensionInbox.MaxConversations + 4}").Should().BeTrue();
    }
}
