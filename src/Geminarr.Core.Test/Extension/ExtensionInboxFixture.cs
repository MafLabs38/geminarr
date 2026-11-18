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
        _subject.RecordList([new ConversationHeader("c1", "T", 0)], Now);
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
        var now = DateTimeOffset.UnixEpoch;
        var heavy = new string('x', ExtensionInbox.MaxHeldCharacters / 2 + 1);
        _subject.RecordConversation("first", "T", [new Turn("user", null, heavy)], now);
        _subject.RecordConversation("second", "T", [new Turn("user", null, heavy)], now.AddSeconds(1));
        _subject.RecordConversation("third", "T", [new Turn("user", null, "short")], now.AddSeconds(2));

        _subject.Has("first").Should().BeFalse("it was the oldest, and the two heavy ones were over the bound");
        _subject.Has("second").Should().BeTrue();
        _subject.Has("third").Should().BeTrue();
    }
}
