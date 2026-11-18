using Geminarr.Core.Extension;
using Geminarr.Core.Pipeline;
using Geminarr.Core.Test.Access;
using Geminarr.Core.Test.Support;
using Microsoft.Extensions.Time.Testing;

namespace Geminarr.Core.Test.Extension;

public sealed class ExtensionServiceFixture : IDisposable
{
    private readonly TestDatabase _workspace = new();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 18, 9, 0, 0, TimeSpan.Zero));
    private readonly ExtensionInbox _inbox = new();
    private readonly PassTrigger _trigger = new();
    private readonly ConversationStore _conversations;
    private readonly OutboxStore _outbox;

    public ExtensionServiceFixture()
    {
        _conversations = new ConversationStore(_workspace.Database);
        _outbox = new OutboxStore(_workspace.Database);
    }

    public void Dispose()
    {
        _trigger.Dispose();
        _workspace.Dispose();
    }

    private ExtensionService Subject(ReaderKind kind = ReaderKind.Web, int maxOpen = 5) =>
        new(_inbox, _conversations, _outbox, _trigger, Options.Of(new ReaderOptions { Kind = kind, MaxOpenPerPass = maxOpen }), _clock);

    private static PushedItem Item(string? id, string? title = "T") => new(id, title);

    private static ExtensionPushRequest ListOf(params PushedItem[] items) => new("list", null, null, null, items, null, "1.0.0");

    private static ExtensionPushRequest ConversationOf(string? id, params PushedTurn[] turns) => new("conversation", null, id, "A title", null, turns, "1.0.0");

    [Theory]
    [InlineData(ReaderKind.None)]
    [InlineData(ReaderKind.Demo)]
    public void should_refuse_everything_when_the_reader_is_not_the_web_one(ReaderKind kind)
    {
        var result = Subject(kind).Handle(new ExtensionPushRequest("state", true, null, null, null, null, null));

        result.Accepted.Should().BeFalse();
        result.Problem.Should().Contain("GEMINARR__READER__KIND");
        _inbox.Session.Should().BeNull();
    }

    [Fact]
    public void should_refuse_a_kind_that_it_does_not_know_and_a_state_that_says_nothing()
    {
        Subject().Handle(new ExtensionPushRequest("something", null, null, null, null, null, null)).Accepted.Should().BeFalse();
        Subject().Handle(new ExtensionPushRequest(null, null, null, null, null, null, null)).Accepted.Should().BeFalse();
        Subject().Handle(new ExtensionPushRequest("state", null, null, null, null, null, null)).Accepted.Should().BeFalse();
    }

    [Theory]
    [InlineData(true, "1.2.3", "1.2.3")]
    [InlineData(false, "1.0.0-rc.1+a", "1.0.0-rc.1+a")]
    [InlineData(true, "a version that is a sentence", null)]
    [InlineData(true, "", null)]
    public void should_record_the_state_with_a_version_that_is_a_version_and_nothing_else(bool signedIn, string version, string? kept)
    {
        var result = Subject().Handle(new ExtensionPushRequest("state", signedIn, null, null, null, null, version));

        result.Accepted.Should().BeTrue();
        _inbox.Session.Should().Be(new ExtensionSession(signedIn, kept, _clock.GetUtcNow()));
    }

    [Fact]
    public async Task should_ask_for_a_pass_when_the_page_says_that_nobody_is_signed_in_and_not_when_it_says_the_same_again()
    {
        var subject = Subject();
        subject.Handle(new ExtensionPushRequest("state", true, null, null, null, null, null));
        (await _trigger.WaitAsync(TimeSpan.Zero, TestContext.Current.CancellationToken)).Should().BeTrue("the first word is a change");

        subject.Handle(new ExtensionPushRequest("state", true, null, null, null, null, null));
        (await _trigger.WaitAsync(TimeSpan.Zero, TestContext.Current.CancellationToken)).Should().BeFalse("the same word again changes nothing");

        subject.Handle(new ExtensionPushRequest("state", false, null, null, null, null, null));
        (await _trigger.WaitAsync(TimeSpan.Zero, TestContext.Current.CancellationToken)).Should().BeTrue();
    }

    [Fact]
    public async Task should_ask_for_a_pass_when_a_list_comes_after_a_page_that_nobody_was_signed_in_to_and_not_otherwise()
    {
        var subject = Subject();
        subject.Handle(ListOf(Item("a")));
        (await _trigger.WaitAsync(TimeSpan.Zero, TestContext.Current.CancellationToken)).Should().BeFalse("a list brings no news until a conversation comes with it");

        subject.Handle(new ExtensionPushRequest("state", false, null, null, null, null, null));
        await _trigger.WaitAsync(TimeSpan.Zero, TestContext.Current.CancellationToken);
        subject.Handle(ListOf(Item("a")));

        (await _trigger.WaitAsync(TimeSpan.Zero, TestContext.Current.CancellationToken)).Should().BeTrue("somebody signed in again");
    }

    [Fact]
    public void should_ask_for_the_conversations_that_are_new_and_not_for_the_ones_that_it_has()
    {
        _inbox.RecordConversation("have", "T", [new Turn("user", null, "x")], _clock.GetUtcNow());

        var result = Subject().Handle(ListOf(Item("have"), Item("new1"), Item("new2")));

        result.Accepted.Should().BeTrue();
        result.Open.Should().Equal("new1", "new2");
        _inbox.List().Select(header => (header.Id, header.Rank)).Should().Equal(("have", 0), ("new1", 1), ("new2", 2));
        _inbox.Session!.SignedIn.Should().BeTrue("a list is the page of an account");
    }

    [Fact]
    public void should_ask_for_no_more_than_a_pass_opens_and_the_most_recent_first()
    {
        var result = Subject(maxOpen: 2).Handle(ListOf(Item("a"), Item("b"), Item("c")));

        result.Open.Should().Equal("a", "b");
    }

    [Fact]
    public void should_not_ask_again_for_a_conversation_that_has_not_changed()
    {
        _conversations.Save(new ConversationRecord("old", ConversationLines.TitleHashOf("T"), _clock.GetUtcNow(), _clock.GetUtcNow(), _clock.GetUtcNow(), 3, 2, 2, null, 0, "web", ConversationStates.Synced, null));

        var result = Subject().Handle(ListOf(Item("x0"), Item("x1"), Item("x2"), Item("old")));

        result.Open.Should().Equal("x0", "x1", "x2");
        result.Open.Should().NotContain("old");
    }

    [Fact]
    public void should_forget_a_duplicate_in_a_list_and_keep_the_first_place()
    {
        Subject().Handle(ListOf(Item("a"), Item("b"), Item("a")));

        _inbox.List().Select(header => header.Id).Should().Equal("a", "b");
    }

    [Theory]
    [InlineData("has space")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("a/../b")]
    public void should_refuse_a_list_with_an_id_that_is_not_one(string? id)
    {
        Subject().Handle(ListOf(Item("fine"), Item(id))).Accepted.Should().BeFalse();
        _inbox.List().Should().BeEmpty("nothing of a refused list is kept");
    }

    [Fact]
    public void should_refuse_a_list_that_is_missing_or_too_long()
    {
        Subject().Handle(new ExtensionPushRequest("list", null, null, null, null, null, null)).Accepted.Should().BeFalse();
        Subject().Handle(ListOf([.. Enumerable.Range(0, ExtensionService.MaxListed + 1).Select(i => Item($"c{i}"))])).Accepted.Should().BeFalse();
    }

    [Fact]
    public void should_cut_a_title_that_is_too_long()
    {
        Subject().Handle(ListOf(Item("a", new string('t', ExtensionService.MaxTitleLength + 50))));

        _inbox.List().Single().Title!.Length.Should().Be(ExtensionService.MaxTitleLength);
    }

    [Fact]
    public async Task should_take_a_conversation_and_ask_for_a_pass_at_once()
    {
        var result = Subject().Handle(ConversationOf("c1", new PushedTurn("user", "hi"), new PushedTurn("assistant", "hello")));

        result.Accepted.Should().BeTrue();
        _inbox.Take("c1")!.Select(turn => (turn.Role, turn.Text)).Should().Equal(("user", "hi"), ("assistant", "hello"));
        (await _trigger.WaitAsync(TimeSpan.Zero, TestContext.Current.CancellationToken)).Should().BeTrue("the pass runs now and does not wait for its hour");
    }

    [Theory]
    [InlineData("tool", "text")]
    [InlineData("user", "")]
    [InlineData("user", "   ")]
    [InlineData(null, "text")]
    [InlineData("user", null)]
    public void should_refuse_a_turn_that_is_not_one(string? role, string? text)
    {
        var result = Subject().Handle(ConversationOf("c1", new PushedTurn("user", "fine"), new PushedTurn(role, text)));

        result.Accepted.Should().BeFalse();
        _inbox.Has("c1").Should().BeFalse("nothing of a refused conversation is kept");
    }

    [Fact]
    public void should_refuse_a_conversation_with_no_turns_too_many_or_too_long()
    {
        Subject().Handle(ConversationOf("c1")).Accepted.Should().BeFalse();
        Subject().Handle(new ExtensionPushRequest("conversation", null, "c1", null, null, null, null)).Accepted.Should().BeFalse();
        Subject().Handle(ConversationOf("c1", [.. Enumerable.Range(0, ExtensionService.MaxTurns + 1).Select(_ => new PushedTurn("user", "x"))])).Accepted.Should().BeFalse();
        Subject().Handle(ConversationOf("c1", new PushedTurn("user", new string('x', ExtensionService.MaxTurnLength + 1)))).Accepted.Should().BeFalse();
        var heavy = Enumerable.Range(0, 11).Select(_ => new PushedTurn("user", new string('x', ExtensionService.MaxTurnLength))).ToArray();
        Subject().Handle(ConversationOf("c1", heavy)).Accepted.Should().BeFalse("the whole conversation is bounded too");
    }

    [Fact]
    public void should_refuse_a_conversation_with_an_id_that_is_not_one()
    {
        Subject().Handle(ConversationOf("not an id", new PushedTurn("user", "x"))).Accepted.Should().BeFalse();
        Subject().Handle(ConversationOf(null, new PushedTurn("user", "x"))).Accepted.Should().BeFalse();
    }

    [Fact]
    public void should_not_say_in_its_answer_what_it_was_told()
    {
        var result = Subject().Handle(ConversationOf("c1", new PushedTurn("tool", "a secret sentence of a conversation")));

        result.Problem.Should().NotContain("secret").And.NotContain("c1");
    }
}
