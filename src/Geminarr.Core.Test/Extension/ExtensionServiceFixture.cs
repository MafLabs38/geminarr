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

    private ExtensionService Subject(ReaderKind kind = ReaderKind.Web, int maxOpen = 5, int maxListed = 40, int roundMinutes = 15) =>
        new(_inbox, _conversations, _outbox, _trigger, Options.Of(new ReaderOptions { Kind = kind, MaxOpenPerPass = maxOpen, MaxListed = maxListed }), Options.Of(new ExtensionOptions { RoundMinutes = roundMinutes }), _clock);

    private static PushedItem Item(string? id, string? title = "T") => new(id, title);

    /// <summary>Geminarr asks for a conversation by answering a list that holds it: only then does it take it.</summary>
    private void AskFor(params string[] ids) => Subject().Handle(ListOf([.. ids.Select(id => Item(id))]));

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
        Subject().Handle(ListOf([.. Enumerable.Range(0, ExtensionService.MaxPushedItems + 1).Select(i => Item($"c{i}"))])).Accepted.Should().BeFalse();
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
        AskFor("c1");
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
        AskFor("c1");
        var result = Subject().Handle(ConversationOf("c1", new PushedTurn("user", "fine"), new PushedTurn(role, text)));

        result.Accepted.Should().BeFalse();
        _inbox.Has("c1").Should().BeFalse("nothing of a refused conversation is kept");
    }

    [Fact]
    public void should_refuse_a_conversation_with_no_turns_too_many_or_too_long()
    {
        AskFor("c1");
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
        AskFor("c1");
        var result = Subject().Handle(ConversationOf("c1", new PushedTurn("tool", "a secret sentence of a conversation")));

        result.Problem.Should().NotContain("secret").And.NotContain("c1");
    }

    [Theory]
    [InlineData(ExtensionBrokenSteps.Page)]
    [InlineData(ExtensionBrokenSteps.List)]
    public async Task should_take_the_word_that_the_page_is_not_what_the_extension_knows_and_ask_for_a_pass_at_once(string step)
    {
        var result = Subject().Handle(new ExtensionPushRequest("broken", null, null, null, null, null, "1.0.0", step));

        result.Accepted.Should().BeTrue();
        _inbox.Session.Should().Be(new ExtensionSession(true, "1.0.0", _clock.GetUtcNow(), step));
        (await _trigger.WaitAsync(TimeSpan.Zero, TestContext.Current.CancellationToken)).Should().BeTrue("the page says it at once");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Page")]
    [InlineData("a sentence that the page said")]
    public void should_refuse_a_broken_page_at_a_step_that_is_not_one_of_the_three_and_not_repeat_it(string? step)
    {
        var result = Subject().Handle(new ExtensionPushRequest("broken", null, null, null, null, null, null, step));

        result.Accepted.Should().BeFalse();
        result.Problem.Should().NotContain("sentence");
        _inbox.Session.Should().BeNull();
    }

    [Fact]
    public async Task should_ask_for_a_pass_when_the_page_is_known_again_after_it_was_not_even_if_nobody_changed_sides()
    {
        var subject = Subject();
        subject.Handle(new ExtensionPushRequest("state", false, null, null, null, null, null));
        await _trigger.WaitAsync(TimeSpan.Zero, TestContext.Current.CancellationToken);
        subject.Handle(new ExtensionPushRequest("broken", null, null, null, null, null, null, ExtensionBrokenSteps.Page));
        await _trigger.WaitAsync(TimeSpan.Zero, TestContext.Current.CancellationToken);

        subject.Handle(new ExtensionPushRequest("state", false, null, null, null, null, null));

        (await _trigger.WaitAsync(TimeSpan.Zero, TestContext.Current.CancellationToken)).Should().BeTrue("a page that is known again is news");
        _inbox.Session!.Broken.Should().BeNull();
    }

    [Fact]
    public void should_choose_what_to_open_only_among_the_conversations_that_a_pass_looks_at()
    {
        var result = Subject(maxListed: 2).Handle(ListOf(Item("a"), Item("b"), Item("c"), Item("d")));

        result.Open.Should().Equal("a", "b");
    }

    [Fact]
    public void should_take_a_list_a_turn_and_a_conversation_of_exactly_the_largest_size_that_is_allowed()
    {
        AskFor("c1", "c2", "c3");
        var subject = Subject();

        subject.Handle(ListOf([.. Enumerable.Range(0, ExtensionService.MaxPushedItems).Select(i => Item($"c{i}"))])).Accepted.Should().BeTrue();
        subject.Handle(ConversationOf("c1", new PushedTurn("user", new string('x', ExtensionService.MaxTurnLength)))).Accepted.Should().BeTrue();
        subject.Handle(ConversationOf("c2", [.. Enumerable.Range(0, ExtensionService.MaxTurns).Select(_ => new PushedTurn("user", "x"))])).Accepted.Should().BeTrue();
        var whole = Enumerable.Range(0, ExtensionService.MaxConversationLength / ExtensionService.MaxTurnLength).Select(_ => new PushedTurn("user", new string('x', ExtensionService.MaxTurnLength))).ToArray();
        subject.Handle(ConversationOf("c3", whole)).Accepted.Should().BeTrue("the whole conversation may be as long as the bound says");
    }

    [Theory]
    [InlineData(64, true)]
    [InlineData(65, false)]
    public void should_take_an_id_of_sixty_four_characters_and_not_of_sixty_five(int length, bool accepted)
    {
        var id = new string('a', length);

        Subject().Handle(ListOf(Item(id))).Accepted.Should().Be(accepted);
        Subject().Handle(ConversationOf(id, new PushedTurn("user", "x"))).Accepted.Should().Be(accepted);
    }

    [Fact]
    public void should_refuse_a_conversation_that_it_did_not_ask_for_and_keep_nothing_of_it()
    {
        var result = Subject().Handle(ConversationOf("c1", new PushedTurn("user", "hi")));

        result.Accepted.Should().BeFalse();
        _inbox.Has("c1").Should().BeFalse();
    }

    [Fact]
    public void should_take_a_conversation_once_for_each_time_it_asked_for_it()
    {
        AskFor("c1");
        Subject().Handle(ConversationOf("c1", new PushedTurn("user", "hi"))).Accepted.Should().BeTrue();
        _inbox.Take("c1");

        Subject().Handle(ConversationOf("c1", new PushedTurn("user", "hi"))).Accepted.Should().BeFalse("it was taken by the pass: the extension has to be asked again");
    }

    [Fact]
    public void should_forget_what_it_asked_for_and_what_nobody_took_after_an_hour()
    {
        AskFor("c1", "c2");
        Subject().Handle(ConversationOf("c1", new PushedTurn("user", "hi")));
        _clock.Advance(ExtensionInbox.HeldFor + TimeSpan.FromMinutes(1));

        Subject().Handle(ListOf(Item("c3")));

        _inbox.Has("c1").Should().BeFalse("nothing took it in an hour");
        _inbox.WasAsked("c2").Should().BeFalse();
        _inbox.WasAsked("c3").Should().BeTrue();
    }

    [Fact]
    public void should_keep_nothing_of_the_conversations_when_nobody_is_signed_in_any_more()
    {
        AskFor("c1");
        Subject().Handle(ConversationOf("c1", new PushedTurn("user", "hi")));

        Subject().Handle(new ExtensionPushRequest("state", false, null, null, null, null, null));

        _inbox.Has("c1").Should().BeFalse();
        _inbox.WasAsked("c1").Should().BeFalse();
        _inbox.List().Should().BeEmpty();
    }

    [Fact]
    public void should_not_cut_a_character_in_two_when_it_cuts_a_title_and_should_refuse_a_missing_list()
    {
        Subject().Handle(ListOf(Item("a", new string('t', ExtensionService.MaxTitleLength - 1) + "\U0001F600"))).Accepted.Should().BeTrue();
        _inbox.List().Single().Title.Should().Be(new string('t', ExtensionService.MaxTitleLength - 1));

        Subject().Handle(new ExtensionPushRequest("list", null, null, null, null, null, null)).Problem.Should().Be("A list holds items.");
    }

    [Fact]
    public void should_take_a_conversation_whose_title_is_missing_or_too_long_without_telling_it_apart()
    {
        AskFor("c1", "c2");

        Subject().Handle(new ExtensionPushRequest("conversation", null, "c1", null, null, [new PushedTurn("user", "x")], null)).Accepted.Should().BeTrue();
        Subject().Handle(new ExtensionPushRequest("conversation", null, "c2", new string('t', ExtensionService.MaxTitleLength + 50), null, [new PushedTurn("user", "x")], null)).Accepted.Should().BeTrue();
    }

    private ExtensionPushResult NoTurns(string? id) => Subject().Handle(new ExtensionPushRequest("broken", null, id, null, null, null, "1.0.0", ExtensionBrokenSteps.Turns));

    [Fact]
    public async Task should_not_call_the_reader_broken_for_one_conversation_without_a_turn_but_for_three_in_a_row()
    {
        AskFor("c1", "c2", "c3");
        NoTurns("c1").Accepted.Should().BeTrue();
        NoTurns("c2").Accepted.Should().BeTrue();
        (_inbox.Session?.Broken).Should().BeNull("a conversation with nothing to read is not a page that changed");
        (await _trigger.WaitAsync(TimeSpan.Zero, TestContext.Current.CancellationToken)).Should().BeFalse();

        NoTurns("c3").Accepted.Should().BeTrue();

        _inbox.Session!.Broken.Should().Be(ExtensionBrokenSteps.Turns);
        (await _trigger.WaitAsync(TimeSpan.Zero, TestContext.Current.CancellationToken)).Should().BeTrue();
    }

    [Fact]
    public void should_stop_asking_for_a_conversation_that_has_nothing_to_read_and_forget_that_a_conversation_was_read_in_between()
    {
        AskFor("c1", "c3", "c4");
        NoTurns("c1");

        Subject().Handle(ListOf(Item("c2"), Item("c1"))).Open.Should().Equal("c2");

        AskFor("c2");
        Subject().Handle(ConversationOf("c2", new PushedTurn("user", "hi")));
        AskFor("c3", "c4");
        NoTurns("c3");
        NoTurns("c4");
        (_inbox.Session?.Broken).Should().BeNull("one was read after the first: they are not in a row");
    }

    [Fact]
    public void should_mask_a_secret_in_a_turn_before_the_inbox_keeps_it()
    {
        var key = "ghp_" + new string('a', 36);
        AskFor("c1");

        Subject().Handle(new ExtensionPushRequest("conversation", null, "c1", "About " + key, null, [new PushedTurn("user", "export PASSWORD=hunter2hunter2 and " + key)], "1.0.0"));
        var held = _inbox.Take("c1")!;

        held.Single().Text.Should().Be("export PASSWORD=[masked] and [masked:github-token]");
        held.Single().Text.Should().NotContain("hunter2").And.NotContain(key);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not an id")]
    public void should_refuse_a_conversation_without_a_turn_whose_id_is_not_one(string? id)
    {
        NoTurns(id).Accepted.Should().BeFalse();
    }

    [Fact]
    public void should_refuse_the_word_that_a_conversation_has_no_turn_when_it_was_not_asked_for()
    {
        NoTurns("c1").Accepted.Should().BeFalse();
        NoTurns("c2").Accepted.Should().BeFalse();
        NoTurns("c3").Accepted.Should().BeFalse();

        _inbox.Session.Should().BeNull("made-up conversations cannot make the reader broken");
    }

    [Fact]
    public void should_keep_the_turns_broken_when_a_good_list_comes_after_and_say_it_is_no_news()
    {
        AskFor("c1", "c2", "c3");
        NoTurns("c1");
        NoTurns("c2");
        NoTurns("c3");

        Subject().Handle(ListOf(Item("c1"), Item("c4")));

        _inbox.Session!.Broken.Should().Be(ExtensionBrokenSteps.Turns, "a list says nothing of the turns");
    }

    [Fact]
    public void should_refuse_a_list_or_a_conversation_that_holds_a_null_and_keep_nothing()
    {
        Subject().Handle(new ExtensionPushRequest("list", null, null, null, [Item("a"), null], null, null)).Accepted.Should().BeFalse();
        AskFor("c1");
        Subject().Handle(new ExtensionPushRequest("conversation", null, "c1", null, null, [new PushedTurn("user", "x"), null], null)).Accepted.Should().BeFalse();

        _inbox.Has("c1").Should().BeFalse();
    }

    [Fact]
    public void should_keep_who_is_signed_in_from_the_labels_of_the_account_button_and_forget_it_when_nobody_is()
    {
        var subject = Subject();

        subject.Handle(new ExtensionPushRequest("list", null, null, null, [Item("c1a2b3")], null, "1.0.0", Labels: ["Google Account: Jane Doe (jane@example.org)", "not a label"]));

        _inbox.Session!.Account.Should().Be(new Geminarr.Core.Browser.AccountIdentity("Jane Doe", "jane@example.org"));

        subject.Handle(new ExtensionPushRequest("state", false, null, null, null, null, "1.0.0"));

        _inbox.Session!.Account.Should().BeNull();
    }

    [Fact]
    public void should_read_no_more_than_a_few_labels_and_nothing_that_is_not_a_label()
    {
        var labels = Enumerable.Range(0, 20).Select(_ => (string?)"nothing").Append("Google Account: Late (late@example.org)").ToList();

        Subject().Handle(new ExtensionPushRequest("list", null, null, null, [Item("c1a2b3")], null, "1.0.0", Labels: labels));

        _inbox.Session!.Account.Should().BeNull("the label that counts came after the few that are read");
    }

    [Fact]
    public void should_answer_a_heartbeat_with_what_it_asks_of_the_extension_and_take_nothing_else_from_it()
    {
        var subject = Subject();

        var nothingKnown = subject.Handle(new ExtensionPushRequest("hello", null, null, null, null, null, "1.0.0"));
        nothingKnown.Should().BeEquivalentTo(ExtensionPushResult.Hints(round: true, signOut: false), "after a restart nothing is known: a round is asked for");
        _inbox.Session.Should().BeNull("a heartbeat says nothing of the session");

        subject.Handle(new ExtensionPushRequest("state", true, null, null, null, null, "1.0.0"));
        subject.Handle(new ExtensionPushRequest("hello", null, null, null, null, null, "1.0.0")).Should().BeEquivalentTo(ExtensionPushResult.Hints(false, false));

        _inbox.RequestRound();
        subject.Handle(new ExtensionPushRequest("hello", null, null, null, null, null, "1.0.0")).Round.Should().BeTrue();
        subject.Handle(new ExtensionPushRequest("hello", null, null, null, null, null, "1.0.0")).Round.Should().BeFalse("a round that was asked for is asked once");

        _inbox.RequestSignOut();
        subject.Handle(new ExtensionPushRequest("hello", null, null, null, null, null, "1.0.0")).SignOut.Should().BeTrue();
    }

    [Fact]
    public void should_ask_for_rounds_while_a_person_signs_in_and_stop_when_somebody_is()
    {
        var subject = Subject();
        subject.Handle(new ExtensionPushRequest("state", false, null, null, null, null, "1.0.0"));
        _inbox.BeginSigningIn(_clock.GetUtcNow());
        var hello = new ExtensionPushRequest("hello", null, null, null, null, null, "1.0.0");

        subject.Handle(hello).Round.Should().BeFalse("what it said is fresh");
        _clock.Advance(TimeSpan.FromSeconds(25));
        subject.Handle(hello).Round.Should().BeTrue("a person is signing in and what it said is old");

        subject.Handle(new ExtensionPushRequest("state", true, null, null, null, null, "1.0.0"));
        _clock.Advance(TimeSpan.FromSeconds(25));
        subject.Handle(hello).Round.Should().BeFalse("somebody is signed in: the sign-in is over");
        _inbox.SigningIn(_clock.GetUtcNow()).Should().BeFalse();
    }

    [Fact]
    public void should_give_up_a_pending_sign_out_only_when_the_operator_cancels_it()
    {
        var subject = Subject();
        var hello = new ExtensionPushRequest("hello", null, null, null, null, null, "1.0.0");
        subject.Handle(new ExtensionPushRequest("state", true, null, null, null, null, "1.0.0"));
        _inbox.RequestSignOut();
        subject.Handle(hello).SignOut.Should().BeTrue();

        _inbox.BeginSigningIn(_clock.GetUtcNow());
        subject.Handle(hello).SignOut.Should().BeTrue("a person opening the desktop does not give a pending sign-out up");

        _inbox.CancelSignOut();

        subject.Handle(hello).SignOut.Should().BeFalse("a sign-out that never completes must not be a loop that nothing can stop");
        _inbox.SignOutPending.Should().BeFalse();
        subject.Handle(new ExtensionPushRequest("state", true, null, null, null, null, "1.0.0"));
        _inbox.Disconnected.Should().BeFalse("somebody signed in");
    }

    [Fact]
    public void should_not_say_signing_in_after_the_operator_disconnected()
    {
        var subject = Subject();
        subject.Handle(new ExtensionPushRequest("state", false, null, null, null, null, "1.0.0"));
        _inbox.BeginSigningIn(_clock.GetUtcNow());
        _inbox.SigningIn(_clock.GetUtcNow()).Should().BeTrue();

        subject.Handle(new ExtensionPushRequest("state", true, null, null, null, null, "1.0.0"));
        _inbox.BeginSigningIn(_clock.GetUtcNow());
        _clock.Advance(TimeSpan.FromSeconds(1));
        _inbox.RequestSignOut();

        _inbox.SigningIn(_clock.GetUtcNow()).Should().BeFalse("a person is not signing in: the operator has just signed the account out");
    }

    [Fact]
    public void should_not_take_a_round_that_began_before_the_sign_out_for_somebody_signing_in_again()
    {
        var subject = Subject();
        var hello = new ExtensionPushRequest("hello", null, null, null, null, null, "1.0.0");
        subject.Handle(new ExtensionPushRequest("state", true, null, null, null, null, "1.0.0"));
        _inbox.RequestSignOut();

        // The list of a round that read the page before the cookies were deleted arrives after the operator disconnected.
        subject.Handle(ListOf(Item("c1a2b3")));

        _inbox.Disconnected.Should().BeTrue("the page that was read is the page before the sign-out");
        subject.Handle(hello).SignOut.Should().BeTrue("the extension is still asked to sign the account out");

        subject.Handle(new ExtensionPushRequest("state", false, null, null, null, null, "1.0.0"));
        _inbox.Disconnected.Should().BeTrue("nobody is signed in: still disconnected");
        subject.Handle(ListOf(Item("c1a2b3")));
        _inbox.Disconnected.Should().BeFalse("somebody signed in after the sign-out was over");
    }

    [Fact]
    public void should_ask_for_the_sign_out_until_the_extension_says_that_nobody_is_signed_in_however_long_that_takes()
    {
        var subject = Subject();
        var hello = new ExtensionPushRequest("hello", null, null, null, null, null, "1.0.0");
        subject.Handle(new ExtensionPushRequest("state", true, null, null, null, null, "1.0.0"));
        _inbox.RequestSignOut();
        subject.Handle(hello).SignOut.Should().BeTrue();
        _inbox.SignOutPending.Should().BeTrue();

        _clock.Advance(TimeSpan.FromHours(3));
        subject.Handle(hello).SignOut.Should().BeTrue("the browser may have been off: the request is not dropped after a while");

        subject.Handle(new ExtensionPushRequest("state", false, null, null, null, null, "1.0.0"));
        subject.Handle(hello).SignOut.Should().BeFalse("nobody is signed in: it is done");
        _inbox.SignOutPending.Should().BeFalse();
        _inbox.Disconnected.Should().BeTrue("and nothing is read until somebody signs in again");
    }

    [Fact]
    public void should_stop_saying_that_a_person_is_signing_in_after_ten_minutes()
    {
        var subject = Subject();
        var hello = new ExtensionPushRequest("hello", null, null, null, null, null, "1.0.0");
        subject.Handle(new ExtensionPushRequest("state", false, null, null, null, null, "1.0.0"));
        _inbox.BeginSigningIn(_clock.GetUtcNow());

        _clock.Advance(TimeSpan.FromMinutes(10) + TimeSpan.FromSeconds(1));

        _inbox.SigningIn(_clock.GetUtcNow()).Should().BeFalse();
        subject.Handle(hello).Round.Should().BeFalse("what it said is older than twenty seconds, but nobody is signing in any more, and the rounds are every fifteen minutes");
    }

    [Fact]
    public void should_not_take_a_conversation_that_was_asked_for_before_the_operator_disconnected()
    {
        var subject = Subject();
        AskFor("c1a2b3");

        _inbox.RequestSignOut();

        subject.Handle(ConversationOf("c1a2b3", new PushedTurn("user", "Hello"))).Accepted.Should().BeFalse("nothing is asked any longer after a disconnect");
    }

    [Fact]
    public void should_ask_for_a_round_when_what_the_extension_said_last_is_older_than_the_interval_of_the_rounds()
    {
        var subject = Subject(roundMinutes: 10);
        var hello = new ExtensionPushRequest("hello", null, null, null, null, null, "1.0.0");
        subject.Handle(new ExtensionPushRequest("state", true, null, null, null, null, "1.0.0"));

        _clock.Advance(TimeSpan.FromMinutes(9));
        subject.Handle(hello).Round.Should().BeFalse("it said something nine minutes ago");

        _clock.Advance(TimeSpan.FromMinutes(1));
        subject.Handle(hello).Round.Should().BeTrue("ten minutes: a round is due");
    }

    [Fact]
    public void should_mask_a_secret_in_a_title_that_straddles_the_cut_before_cutting()
    {
        // Built here: no file holds the shape of a token.
        var secret = "gh" + "p_" + new string('a', 36);
        var title = new string('x', ExtensionService.MaxTitleLength - 10) + " " + secret + " and more";

        Subject().Handle(ListOf(Item("c1a2b3", title))).Accepted.Should().BeTrue();

        var kept = _inbox.List().Single().Title!;
        kept.Length.Should().BeLessThanOrEqualTo(ExtensionService.MaxTitleLength);
        kept.Should().NotContain("gh" + "p_", "no head of the token stays when the cut falls in the middle of it");
        kept.Should().Contain("[masked");
    }

    [Theory]
    [InlineData(32, true)]
    [InlineData(33, false)]
    public void should_keep_a_version_of_at_most_the_longest_length(int length, bool kept)
    {
        var version = "1." + new string('0', length - 2);

        Subject().Handle(new ExtensionPushRequest("state", true, null, null, null, null, version)).Accepted.Should().BeTrue();

        (_inbox.Session!.Version is not null).Should().Be(kept);
    }

    [Fact]
    public void should_take_a_null_label_for_no_label_and_read_the_one_that_follows()
    {
        Subject().Handle(new ExtensionPushRequest("list", null, null, null, [Item("c1a2b3")], null, "1.0.0", Labels: [null, "Google Account: Jane Doe (jane@example.org)"]));

        _inbox.Session!.Account.Should().Be(new Geminarr.Core.Browser.AccountIdentity("Jane Doe", "jane@example.org"));
    }

    [Fact]
    public void should_ask_for_nothing_and_take_nothing_while_the_operators_disconnect_stands()
    {
        var subject = Subject();
        subject.Handle(ListOf(Item("c1a2b3")));
        AskFor("c1a2b3");
        _inbox.RequestSignOut();

        var list = subject.Handle(ListOf(Item("c1a2b3"), Item("d2b3c4")));

        list.Accepted.Should().BeTrue();
        list.Open.Should().BeEmpty("the page that is shown is not a session that is read, whatever it shows");
        AskFor("c1a2b3");
        subject.Handle(ConversationOf("c1a2b3", new PushedTurn("user", "Hello"))).Accepted.Should().BeFalse("a conversation that arrives after the disconnect is refused");
    }
}
