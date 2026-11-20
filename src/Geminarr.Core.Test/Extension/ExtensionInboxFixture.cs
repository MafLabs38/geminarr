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
        _subject.Take("c1")!.Turns.Select(turn => turn.Text).Should().Equal("hi", "hello");
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

        _subject.Take("c1")!.Turns.Should().HaveCount(2);
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
    public void should_not_flip_the_page_between_broken_and_known_when_three_textless_conversations_come_with_a_readable_one()
    {
        _subject.RecordSession(signedIn: true, null, Now);
        var readable = new[] { new Turn("user", null, "hi") };

        // Round one: the three textless ones answer first, then the readable one.
        _subject.RecordUnreadable("v1", null, Now).Should().BeFalse();
        _subject.RecordUnreadable("v2", null, Now).Should().BeFalse();
        _subject.RecordUnreadable("v3", null, Now).Should().BeTrue();
        _subject.RecordConversation("r0", "T", readable, Now);
        _subject.Session!.Broken.Should().BeNull();

        // Round two, a quarter of an hour later: the three are not asked for again, so nothing can break the page again.
        var later = Now.AddMinutes(15);
        new[] { "v1", "v2", "v3" }.Should().OnlyContain(id => _subject.IsUnreadable(Header(id)));
        _subject.RecordConversation("r0", "T", readable, later);
        _subject.Session!.Broken.Should().BeNull();

        // A new textless conversation alone is one, not three in a row.
        _subject.RecordUnreadable("v4", null, later).Should().BeFalse();
    }

    [Fact]
    public void should_keep_an_unreadable_one_while_it_is_as_it_was_and_forget_it_when_it_changes_and_when_nobody_is_signed_in()
    {
        _subject.RecordList([new ConversationHeader("c0", "Zero", 0), new ConversationHeader("c1", "One", 3)], null, Now);
        _subject.RecordUnreadable("c1", null, Now);
        _subject.RecordConversation("c9", "T", [new Turn("user", null, "hi")], Now);

        _subject.IsUnreadable(new ConversationHeader("c1", "One", 3)).Should().BeTrue("a read does not clear it");
        _subject.IsUnreadable(new ConversationHeader("c1", "One", 7)).Should().BeTrue("further down the list is still as it was");
        _subject.IsUnreadable(new ConversationHeader("c1", "One", 0)).Should().BeFalse("moved up: it was continued");
        _subject.IsUnreadable(new ConversationHeader("c1", "Renamed", 3)).Should().BeFalse("renamed");
        _subject.IsUnreadable(new ConversationHeader("c2", "One", 3)).Should().BeFalse("another conversation");

        _subject.RecordConversation("c8", "T", [new Turn("user", null, "hi")], Now.Add(ExtensionInbox.HeldFor).AddMinutes(1));
        _subject.IsUnreadable(new ConversationHeader("c1", "One", 3)).Should().BeTrue("an hour is not a reason to open it again");

        _subject.RecordSession(signedIn: false, null, Now);
        _subject.IsUnreadable(new ConversationHeader("c1", "One", 3)).Should().BeFalse();
    }

    [Fact]
    public void should_ask_again_for_the_conversations_marked_during_a_broken_page_once_a_new_version_reads_one()
    {
        _subject.RecordSession(signedIn: true, null, Now);
        foreach (var id in new[] { "v1", "v2", "v3", "v4" })
        {
            _subject.RecordUnreadable(id, "1.0.0", Now);
        }

        _subject.IsUnreadable(new ConversationHeader("v1", null, 4)).Should().BeFalse("a run of three says something about the page, not about its conversations");
        _subject.IsUnreadable(new ConversationHeader("v4", null, 4)).Should().BeFalse("nor does one that came after the break");

        _subject.RecordConversation("r0", "T", [new Turn("user", null, "hi")], Now, "1.0.1");

        _subject.Session!.Broken.Should().BeNull();
        new[] { "v1", "v2", "v3", "v4" }.Should().OnlyContain(id => !_subject.IsUnreadable(new ConversationHeader(id, null, 4)), "a fixed extension reads them again");
    }

    [Fact]
    public void should_ask_again_for_every_marked_conversation_when_another_version_of_the_extension_runs()
    {
        _subject.RecordList([new ConversationHeader("c0", "Zero", 0), new ConversationHeader("c1", "One", 3)], "1.0.0", Now);
        _subject.RecordUnreadable("c1", "1.0.0", Now);
        _subject.IsUnreadable(new ConversationHeader("c1", "One", 3)).Should().BeTrue();

        _subject.RecordList([new ConversationHeader("c0", "Zero", 0), new ConversationHeader("c1", "One", 3)], "1.0.1", Now);

        _subject.IsUnreadable(new ConversationHeader("c1", "One", 3)).Should().BeFalse("a fixed extension may read what the old one could not");
    }

    [Fact]
    public void should_never_exclude_the_most_recent_conversation_whatever_it_showed()
    {
        _subject.RecordList([new ConversationHeader("c0", "Zero", 0)], null, Now);
        _subject.RecordUnreadable("c0", null, Now);

        _subject.IsUnreadable(new ConversationHeader("c0", "Zero", 0)).Should().BeFalse("it cannot move up, and a person may have added text to it");
    }

    [Fact]
    public void should_not_take_three_conversations_without_a_turn_for_a_page_that_changed_when_they_are_more_than_an_hour_apart()
    {
        _subject.RecordSession(signedIn: true, null, Now);

        _subject.RecordUnreadable("v1", null, Now).Should().BeFalse();
        _subject.RecordUnreadable("v2", null, Now.AddMinutes(40)).Should().BeFalse();
        _subject.RecordUnreadable("v3", null, Now.Add(ExtensionInbox.HeldFor).AddMinutes(5)).Should().BeFalse("the first one is more than an hour old");
        _subject.Session!.Broken.Should().BeNull();
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
            // A conversation that is read in between: they are not three in a row, and each stays marked.
            _subject.RecordConversation($"r{i}", "T", [new Turn("user", null, "hi")], Now.AddSeconds(i));
        }

        _subject.IsUnreadable(Header("c0")).Should().BeFalse();
        _subject.IsUnreadable(Header($"c{ExtensionInbox.MaxConversations + 4}")).Should().BeTrue();
    }

    private static ConversationHeader Header(string id) => new(id, null, 1);

    private static string[] Labels(string address) => [$"Google Account: Someone ({address})"];

    [Fact]
    public void should_not_hand_what_was_read_for_one_account_to_the_next_one_that_signs_in()
    {
        _subject.RecordSession(signedIn: true, null, Now, Labels("jane.doe@example.org"));
        _subject.Ask(["c1"], Now);
        _subject.RecordConversation("c1", "T", [new Turn("user", null, "hi")], Now);

        // Another account is the one on the page before the extension said that nobody was signed in.
        _subject.RecordSession(signedIn: true, null, Now.AddMinutes(1), Labels("john.roe@example.org"));

        _subject.Has("c1").Should().BeFalse();
        _subject.WasAsked("c1").Should().BeFalse();
        _subject.Session!.Account!.Email.Should().Be("john.roe@example.org");
    }

    [Fact]
    public void should_keep_what_was_read_while_the_same_account_is_said_again_in_another_case()
    {
        _subject.RecordSession(signedIn: true, null, Now, Labels("jane.doe@example.org"));
        _subject.RecordConversation("c1", "T", [new Turn("user", null, "hi")], Now);

        _subject.RecordList([new ConversationHeader("c2", "T", 0)], null, Now.AddMinutes(1), Labels("Jane.Doe@Example.org"));

        _subject.Has("c1").Should().BeTrue();
    }

    [Fact]
    public void should_say_no_account_for_a_list_whose_label_says_nothing_and_keep_it_for_a_list_without_labels()
    {
        _subject.RecordSession(signedIn: true, null, Now, Labels("jane.doe@example.org"));

        _subject.RecordList([], null, Now.AddMinutes(1));
        _subject.Session!.Account!.Email.Should().Be("jane.doe@example.org", "a list that gave no label says nothing about who is signed in");

        _subject.RecordList([], null, Now.AddMinutes(2), ["Account", ""]);
        _subject.Session!.Account.Should().BeNull("a label that was given and does not name an account is not the account that was there: it is not inherited");
        _subject.Session.SignedIn.Should().BeTrue();
    }

    [Fact]
    public void should_hold_what_is_read_for_the_account_that_comes_back_after_a_label_that_said_nothing()
    {
        _subject.RecordSession(signedIn: true, null, Now, Labels("jane.doe@example.org"));
        _subject.RecordConversation("c1", "T", [new Turn("user", null, "hi")], Now);
        _subject.RecordList([], null, Now.AddMinutes(1), ["Account"]);

        _subject.RecordList([], null, Now.AddMinutes(2), Labels("jane.doe@example.org"));

        _subject.Has("c1").Should().BeTrue("the same account is back: nothing was given to another");
    }
}
