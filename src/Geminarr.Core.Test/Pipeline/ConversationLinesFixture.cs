using System.Text.Json;
using Geminarr.Core.Pipeline;

namespace Geminarr.Core.Test.Pipeline;

public sealed class ConversationLinesFixture
{
    [Fact]
    public void should_name_a_conversation_from_the_id_the_assistant_gave()
    {
        ConversationLines.Session("c1a2b3c4d5e6").Should().Be("gemini-c1a2b3c4d5e6");
        ConversationLines.Session("c1a2-b3").Should().Be("gemini-c1a2-b3");
    }

    [Fact]
    public void should_lower_the_case_and_add_a_short_hash_so_that_two_ids_that_differ_by_case_stay_apart()
    {
        ConversationLines.Session("C1A2B3").Should().MatchRegex("^gemini-c1a2b3-[0-9a-f]{6}$").And.NotBe(ConversationLines.Session("c1a2b3"));
    }

    [Fact]
    public void should_replace_what_a_name_cannot_hold_and_tell_two_different_ids_apart()
    {
        var first = ConversationLines.Session("a/b");
        var second = ConversationLines.Session("a?b");
        var empty = ConversationLines.Session("///");

        first.Should().MatchRegex("^gemini-a-b-[0-9a-f]{6}$");
        second.Should().MatchRegex("^gemini-a-b-[0-9a-f]{6}$").And.NotBe(first);
        empty.Should().MatchRegex("^gemini-c-[0-9a-f]{6}$");
        ConversationLines.Session("a/b").Should().Be(first, "the name of a conversation is stable");
    }

    [Fact]
    public void should_write_the_meta_line_as_line_zero_with_a_title_cut_at_two_hundred_characters()
    {
        using var document = JsonDocument.Parse(ConversationLines.Meta("c1", new string('t', 300), "web"));

        var root = document.RootElement;
        root.GetProperty("v").GetInt32().Should().Be(1);
        root.GetProperty("meta").GetProperty("title").GetString()!.Length.Should().Be(ConversationLines.MaxTitleLength);
        root.GetProperty("meta").GetProperty("source").GetString().Should().Be("web");
        root.GetProperty("meta").GetProperty("id").GetString().Should().Be("c1");
    }

    [Fact]
    public void should_cut_a_turn_so_that_a_line_never_reaches_the_limit_of_mnemarr_and_not_in_the_middle_of_a_pair()
    {
        var text = new string('é', ConversationLines.MaxTurnCharacters - 1) + "😀" + new string('x', 5_000);

        var line = ConversationLines.TurnLine(new Turn("user", null, text));

        System.Text.Encoding.UTF8.GetByteCount(line).Should().BeLessThan(1_500_000);
        using var document = JsonDocument.Parse(line);
        var cut = document.RootElement.GetProperty("text").GetString()!;
        cut.Length.Should().Be(ConversationLines.MaxTurnCharacters - 1, "the pair of characters that would be split is left out");
        cut.Should().NotContain("\uFFFD");
    }

    [Fact]
    public void should_write_an_empty_title_as_it_is()
    {
        using var document = JsonDocument.Parse(ConversationLines.Meta("c1", null, "web"));

        document.RootElement.GetProperty("meta").GetProperty("title").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public void should_write_a_turn_with_its_role_its_moment_in_utc_and_its_text_unchanged()
    {
        using var document = JsonDocument.Parse(ConversationLines.TurnLine(new Turn("assistant", new DateTimeOffset(2026, 11, 11, 9, 3, 12, TimeSpan.FromHours(1)), "Héllo <b>\"x\"</b>\nline two")));

        var root = document.RootElement;
        root.GetProperty("role").GetString().Should().Be("assistant");
        root.GetProperty("at").GetString().Should().Be("2026-11-11T08:03:12Z");
        root.GetProperty("text").GetString().Should().Be("Héllo <b>\"x\"</b>\nline two");
    }

    [Fact]
    public void should_write_no_moment_when_the_product_shows_none()
    {
        using var document = JsonDocument.Parse(ConversationLines.TurnLine(new Turn("user", null, "x")));

        document.RootElement.GetProperty("at").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public void should_hash_a_turn_by_its_role_and_its_text()
    {
        var turn = new Turn("user", null, "same");

        ConversationLines.HashOf(turn).Should().Be(ConversationLines.HashOf(new Turn("user", DateTimeOffset.UnixEpoch, "same")));
        ConversationLines.HashOf(turn).Should().NotBe(ConversationLines.HashOf(new Turn("assistant", null, "same")));
        ConversationLines.HashOf(turn).Should().NotBe(ConversationLines.HashOf(new Turn("user", null, "other")));
        ConversationLines.HashOf(turn).Should().HaveLength(16);
    }

    [Fact]
    public void should_cut_lines_into_batches_of_at_most_five_hundred_lines_with_the_position_of_each()
    {
        var lines = Enumerable.Range(0, 1_200).Select(index => $"line {index}").ToList();

        var batches = ConversationLines.Batches(10, lines);

        batches.Select(batch => (batch.From, batch.Lines.Count)).Should().Equal((10L, 500), (510L, 500), (1_010L, 200));
        batches.SelectMany(batch => batch.Lines).Should().Equal(lines);
    }

    [Fact]
    public void should_cut_lines_into_batches_of_at_most_three_megabytes()
    {
        var big = new string('x', 1_400_000);

        var batches = ConversationLines.Batches(0, [big, big, big]);

        batches.Select(batch => batch.Lines.Count).Should().Equal(2, 1);
        batches[1].From.Should().Be(2);
    }

    [Fact]
    public void should_make_no_batch_of_no_lines()
    {
        ConversationLines.Batches(0, []).Should().BeEmpty();
    }
}
