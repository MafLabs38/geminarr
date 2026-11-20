using Geminarr.Core.Accounts;
using Geminarr.Core.Browser;
using Geminarr.Core.Configuration;
using Geminarr.Core.Connection;
using Geminarr.Core.Link;
using Geminarr.Core.Pipeline;
using Geminarr.Core.Test.Access;
using Geminarr.Core.Test.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Geminarr.Core.Test.Pipeline;

/// <summary>The pass of two accounts that are signed in in turn: the stores follow the account, and what is read for one is never kept for the other (SPEC §8).</summary>
public sealed class AccountsPassFixture : IDisposable
{
    private const string Jane = "jane.doe@example.org";
    private const string John = "john.roe@example.org";

    private readonly TestDatabase _data = new();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 20, 9, 0, 0, TimeSpan.Zero));
    private readonly SwitchableAccount _account = new();
    private readonly FakeReader _reader = new();
    private readonly FakeSink _sink = new();
    private readonly ConversationStore _conversations;
    private readonly RunStore _runs;
    private readonly PassRunner _subject;

    public AccountsPassFixture()
    {
        var workspaces = new AccountWorkspaces(Options.Of(_data.Settings));
        var database = new AccountDatabase(_account, workspaces);
        _conversations = new ConversationStore(database);
        _runs = new RunStore(database);
        var outbox = new OutboxStore(database);
        var tracker = Tracking.Of(new ConnectionStore(_data.Database, _clock), Options.Of(new ReaderOptions { LoginFailures = 2 }), _clock);
        var link = new LinkService(
            Options.Of(new MnemarrOptions { Url = "https://mnemarr.homelab.example" }),
            new LinkStores(new DestinationStore(database), new FakeKeys { Key = "mnm_key" }, outbox, _account, workspaces),
            new MnemarrClient(new FakeHttp(_ => FakeHttp.Status(System.Net.HttpStatusCode.NotFound))),
            _clock,
            NullLogger<LinkService>.Instance);
        var settings = new PassSettings(Options.Of(new ReaderOptions()), Options.Of(new RetentionOptions()), Options.Of(new GeminarrOptions()), new ReaderProvider(_reader), new NoExposure());
        _subject = new PassRunner(_sink, new PassStores(_conversations, outbox, _runs, _account), tracker, link, settings, _clock, NullLogger<PassRunner>.Instance);
    }

    public void Dispose() => _data.Dispose();

    private sealed class NoExposure : IBrowserExposure
    {
        public ExposureReport Check() => ExposureReport.None;
    }

    private IReadOnlyList<string> ConversationsOf(string address)
    {
        using (_account.Use(AccountKey.Of(address)))
        {
            return [.. _conversations.Recent(100).Select(record => record.Id)];
        }
    }

    [Fact]
    public async Task should_keep_what_it_reads_for_an_account_in_the_workspace_of_that_account_alone()
    {
        _account.SignIn(Jane);
        _reader.Say("jane-1", "T", "a", "b");
        await _subject.RunAsync(TestContext.Current.CancellationToken);

        _account.SignIn(John);
        _reader.Conversations.Clear();
        _reader.Say("john-1", "T", "c", "d");
        await _subject.RunAsync(TestContext.Current.CancellationToken);

        ConversationsOf(Jane).Should().Equal("jane-1");
        ConversationsOf(John).Should().Equal("john-1");
    }

    [Fact]
    public async Task should_keep_the_same_conversation_of_two_accounts_apart()
    {
        _reader.Say("c1", "T", "a", "b");
        _account.SignIn(Jane);
        await _subject.RunAsync(TestContext.Current.CancellationToken);
        _reader.Say("c1", "T", "a", "b", "c", "d");
        _account.SignIn(John);
        await _subject.RunAsync(TestContext.Current.CancellationToken);

        using (_account.Use(AccountKey.Of(Jane)))
        {
            _conversations.Get("c1")!.TurnsKnown.Should().Be(2);
        }

        using (_account.Use(AccountKey.Of(John)))
        {
            _conversations.Get("c1")!.TurnsKnown.Should().Be(4);
        }
    }

    [Fact]
    public async Task should_ask_mnemarr_again_what_it_holds_when_the_account_changes()
    {
        _reader.Say("c1", "T", "a", "b");
        _account.SignIn(Jane);
        await _subject.RunAsync(TestContext.Current.CancellationToken);
        await _subject.RunAsync(TestContext.Current.CancellationToken);
        var before = _sink.PositionCalls;
        before.Should().Be(1, "the same account asks once an hour");

        _account.SignIn(John);
        await _subject.RunAsync(TestContext.Current.CancellationToken);

        _sink.PositionCalls.Should().Be(before + 1, "another account has other machines: what the first one holds says nothing of it");
    }

    [Fact]
    public async Task should_remember_the_account_that_is_signed_in_so_that_an_expired_session_is_still_its_own()
    {
        _reader.Say("c1", "T", "a", "b");
        _account.SignIn(Jane);
        await _subject.RunAsync(TestContext.Current.CancellationToken);

        _account.SignIn(null);
        _reader.Session = SessionCheck.SignedOut;
        await _subject.RunAsync(TestContext.Current.CancellationToken);

        _account.Remembered.Should().Be(AccountKey.Of(Jane));
        using (_account.Use(AccountKey.Of(Jane)))
        {
            _runs.Recent(10).Should().HaveCount(2, "the pass of the expired session is a run of the account whose session expired");
        }

        _sink.Heartbeats.Should().HaveCountGreaterThan(1, "its machine tells Mnemarr, which is what sends the mail that asks to sign in again");
    }

    [Fact]
    public async Task should_stop_and_keep_nothing_when_another_account_signs_in_while_a_pass_reads()
    {
        _account.SignIn(Jane);
        _reader.Say("jane-1", "T", "a", "b");
        // The operator signs another account in in the middle of the pass, while the first conversation is being opened.
        _reader.WhileOpening = () => _account.SignIn(John);

        await _subject.RunAsync(TestContext.Current.CancellationToken);

        // The reader follows the session of the browser: what it gave after the switch is John's, and is kept nowhere.
        ConversationsOf(Jane).Should().BeEmpty();
        ConversationsOf(John).Should().BeEmpty("the pass was Jane's from its first line to its last");
        _sink.Sent.Should().BeEmpty();
        using (_account.Use(AccountKey.Of(Jane)))
        {
            _runs.Recent(1).Single().Should().Match<RunRecord>(run => run.Outcome == RunOutcomes.Partial && run.Reason == "account-changed");
        }
    }

    [Fact]
    public async Task should_read_nothing_and_keep_nothing_when_nobody_is_signed_in_and_none_was()
    {
        _reader.Say("c1", "T", "a", "b");
        _reader.Session = SessionCheck.SignedOut;

        await _subject.RunAsync(TestContext.Current.CancellationToken);

        _sink.Sent.Should().BeEmpty();
        new AccountWorkspaces(Options.Of(_data.Settings)).Known().Should().BeEmpty("no workspace is made for nobody");
    }

    [Fact]
    public async Task should_do_nothing_and_leave_the_workspace_as_it_was_once_the_operator_has_disconnected()
    {
        _reader.Say("c1", "T", "a", "b");
        _account.SignIn(Jane);
        await _subject.RunAsync(TestContext.Current.CancellationToken);
        var sent = _sink.Sent.Count;

        _account.SignIn(null);
        _account.Forget();
        _reader.Session = SessionCheck.SignedOut;
        await _subject.RunAsync(TestContext.Current.CancellationToken);

        _sink.Sent.Should().HaveCount(sent);
        ConversationsOf(Jane).Should().Equal("c1");
    }

    [Fact]
    public async Task should_stop_before_reading_anything_when_another_account_signs_in_while_the_list_is_read()
    {
        _account.SignIn(Jane);
        _reader.Say("jane-1", "T", "a", "b");
        _reader.WhileListing = () => _account.SignIn(John);

        await _subject.RunAsync(TestContext.Current.CancellationToken);

        _reader.Opened.Should().BeEmpty("the list is John's: nothing of it is opened for Jane");
        ConversationsOf(Jane).Should().BeEmpty();
        ConversationsOf(John).Should().BeEmpty();
    }

    [Fact]
    public async Task should_keep_what_was_read_before_the_switch_and_nothing_after_it()
    {
        _account.SignIn(Jane);
        _reader.Say("jane-1", "T", "a", "b");
        _reader.Say("jane-2", "T", "a", "b");
        var opened = 0;
        _reader.WhileOpening = () =>
        {
            if (++opened == 2)
            {
                _account.SignIn(John);
            }
        };

        await _subject.RunAsync(TestContext.Current.CancellationToken);

        ConversationsOf(Jane).Should().Equal("jane-1");
        ConversationsOf(John).Should().BeEmpty();
    }

    [Fact]
    public async Task should_leave_a_sign_out_in_the_middle_of_a_pass_to_the_session_and_keep_nothing_after_it()
    {
        _account.SignIn(Jane);
        _reader.Say("jane-1", "T", "a", "b");
        _reader.WhileOpening = () => _account.SignIn(null);

        await _subject.RunAsync(TestContext.Current.CancellationToken);

        ConversationsOf(Jane).Should().BeEmpty();
        using (_account.Use(AccountKey.Of(Jane)))
        {
            _runs.Recent(1).Single().Reason.Should().Be("signed-out");
        }
    }
}
