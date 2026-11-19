using Geminarr.Core.Pipeline;

namespace Geminarr.Core.Test.Pipeline;

public sealed class ConversationAnchorFixture
{
    private static Turn T(string role, string text) => new(role, null, text);

    private static List<Turn> Turns(params string[] texts) => [.. texts.Select((text, index) => T(index % 2 == 0 ? "user" : "assistant", text))];

    [Fact]
    public void should_find_the_last_turns_that_were_sent_in_the_end_of_a_conversation_that_has_more()
    {
        var sent = Turns("a", "b", "c", "d", "e");
        var anchor = ConversationLines.AnchorOf(sent.TakeLast(ConversationLines.AnchorTurns));

        var end = Turns("c", "d", "e", "f", "g");

        ConversationLines.FindAnchor(end, anchor, 3).Should().Be(0, "the turns c, d and e are the first of the end: f and g are new");
    }

    [Fact]
    public void should_not_find_them_when_a_turn_was_rewritten_or_when_the_end_does_not_reach_back_to_them()
    {
        var anchor = ConversationLines.AnchorOf(Turns("a", "b", "c", "d", "e").TakeLast(3));

        ConversationLines.FindAnchor(Turns("c", "d", "E!", "f"), anchor, 3).Should().Be(-1, "the last turn that was sent is not what it was");
        ConversationLines.FindAnchor(Turns("f", "g", "h", "i"), anchor, 3).Should().Be(-1, "the end that was read starts after them");
        ConversationLines.FindAnchor(Turns("d", "e"), anchor, 3).Should().Be(-1, "the end is shorter than the anchor");
    }

    [Fact]
    public void should_not_take_one_turn_for_the_anchor_when_the_same_answer_comes_back_in_the_conversation()
    {
        // "ok" has been said before: the turn alone would match in two places, the three in a row match in one.
        var sent = Turns("q", "ok", "q2", "ok");
        var anchor = ConversationLines.AnchorOf(sent.TakeLast(3));

        var end = Turns("q", "ok", "q2", "ok", "q3", "ok");

        ConversationLines.FindAnchor(end, anchor, 3).Should().Be(1);
    }

    [Fact]
    public void should_take_the_whole_conversation_for_the_anchor_of_a_short_one()
    {
        var sent = Turns("a", "b");
        var anchor = ConversationLines.AnchorOf(sent);

        ConversationLines.FindAnchor(Turns("a", "b", "c"), anchor, 2).Should().Be(0);
    }

    [Fact]
    public void should_look_for_a_length_of_nothing_without_finding_anything()
    {
        ConversationLines.FindAnchor(Turns("a"), "whatever", 0).Should().Be(-1);
    }
}
