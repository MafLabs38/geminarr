using System.Net;
using Geminarr.Core.Accounts;
using Geminarr.Core.Link;
using Geminarr.Core.Pipeline;
using Geminarr.Core.Test.Access;
using Geminarr.Core.Test.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Geminarr.Core.Test.Accounts;

public sealed class AccountServiceFixture : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 9, 0, 0, TimeSpan.Zero);
    private const string Jane = "jane.doe@example.org";
    private const string John = "john.roe@example.org";

    private readonly TestDatabase _data = new();
    private readonly SwitchableAccount _account = new();
    private readonly AccountWorkspaces _workspaces;
    private readonly AccountDatabase _database;
    private readonly AccountService _subject;

    public AccountServiceFixture()
    {
        _workspaces = new AccountWorkspaces(Options.Of(_data.Settings));
        _database = new AccountDatabase(_account, _workspaces);
        var keys = new KeyStore(Options.Of(new MnemarrOptions()), _account, _workspaces);
        var link = new LinkService(
            Options.Of(new MnemarrOptions()),
            new LinkStores(new DestinationStore(_database), keys, new OutboxStore(_database), _account, _workspaces),
            new MnemarrClient(new FakeHttp(_ => FakeHttp.Status(HttpStatusCode.NotFound))),
            new FakeTimeProvider(Now),
            NullLogger<LinkService>.Instance);
        _subject = new AccountService(_account, _workspaces, link, keys);
    }

    public void Dispose() => _data.Dispose();

    private void Read(string address, params string[] ids)
    {
        _account.SignIn(address);
        var conversations = new ConversationStore(_database);
        foreach (var id in ids)
        {
            conversations.Save(new ConversationRecord(id, null, Now, Now, Now, 0, 2, 2, null, 0, ConversationSources.Web, ConversationStates.Synced, null));
        }
    }

    [Fact]
    public void should_know_the_account_that_is_signed_in_even_before_it_has_a_workspace()
    {
        _account.SignIn(Jane);

        var account = _subject.List().Should().ContainSingle().Subject;

        (account.Active, account.SignedIn, account.Conversations, account.Linked).Should().Be((true, true, 0, false));
        account.Machine.Should().Be("geminarr-jane-doe");
        _workspaces.Known().Should().BeEmpty("listing makes nothing");
    }

    [Fact]
    public void should_list_each_account_with_what_it_holds_and_say_which_is_the_active_one()
    {
        Read(Jane, "c1", "c2");
        Read(John, "c1");

        var accounts = _subject.List();

        accounts.Should().HaveCount(2);
        accounts.Single(account => account.Active).Conversations.Should().Be(1);
        accounts.Single(account => !account.Active).Conversations.Should().Be(2);
        accounts.Select(account => account.Id).Should().OnlyHaveUniqueItems();
        accounts.Should().OnlyContain(account => account.Id.Length == AccountService.IdLength);
    }

    [Fact]
    public void should_keep_the_account_that_expired_as_the_active_one_and_the_other_as_known()
    {
        Read(Jane, "c1");
        Read(John, "c1");
        _account.SignIn(null);

        var accounts = _subject.List();

        accounts.Single(account => account.Active).Id.Should().Be(AccountService.IdOf(AccountKey.Of(John)), "the account that was signed in last is the one that tells Mnemarr");
        accounts.Should().OnlyContain(account => !account.SignedIn);
    }

    [Fact]
    public void should_forget_an_account_that_is_not_the_active_one_and_keep_the_other()
    {
        Read(Jane, "c1");
        Read(John, "c1");
        var jane = AccountService.IdOf(AccountKey.Of(Jane));

        _subject.Forget(jane).Should().Be(ForgetResult.Forgotten);

        _workspaces.Exists(AccountKey.Of(Jane)).Should().BeFalse();
        _workspaces.Exists(AccountKey.Of(John)).Should().BeTrue();
    }

    [Fact]
    public void should_not_forget_the_active_account_nor_one_that_is_not_known()
    {
        Read(Jane, "c1");

        _subject.Forget(AccountService.IdOf(AccountKey.Of(Jane))).Should().Be(ForgetResult.Active);
        _subject.Forget("ffffffff").Should().Be(ForgetResult.Unknown);
        _workspaces.Exists(AccountKey.Of(Jane)).Should().BeTrue();
    }

    [Fact]
    public void should_forget_an_account_once_the_operator_disconnected()
    {
        Read(Jane, "c1");
        _account.SignIn(null);
        _account.Forget();

        _subject.Forget(AccountService.IdOf(AccountKey.Of(Jane))).Should().Be(ForgetResult.Forgotten);
    }

    [Fact]
    public void should_read_the_conversations_of_another_account_from_its_own_workspace()
    {
        Read(Jane, "c1", "c2");
        Read(John, "c3");

        var jane = _subject.ConversationsOf(AccountKey.Of(Jane));

        jane.Count().Should().Be(2);
        _subject.KeyOf(AccountService.IdOf(AccountKey.Of(Jane))).Should().Be(AccountKey.Of(Jane));
        _subject.KeyOf("ffffffff").Should().BeNull();
    }

    [Fact]
    public void should_show_what_a_signed_out_account_holds_its_machine_its_link_and_what_waits()
    {
        Read(John, "c1");
        var destination = new DestinationStore(_database);
        destination.EnsureAddress("https://mnemarr.homelab.example");
        destination.SaveLinked("geminarr-john-roe", Now);
        new OutboxStore(_database).Replace("c1", [(0, ["a line", "another"])], Now);
        var key = AccountKey.Of(John);
        Directory.CreateDirectory(Path.Combine(_workspaces.Folder(key), "mnemarr"));
        File.WriteAllText(Path.Combine(_workspaces.Folder(key), "mnemarr", "key"), "mnm_john");
        _account.SignIn(Jane);

        var john = _subject.List().Single(account => !account.Active);

        (john.Machine, john.Linked, john.WaitingLines, john.Conversations, john.SignedIn).Should().Be(("geminarr-john-roe", true, 2, 1, false));
        john.LastReadAt.Should().NotBeNull();
    }

    [Fact]
    public void should_not_name_an_account_by_an_id_that_two_accounts_share()
    {
        var first = Directory.CreateDirectory(Path.Combine(_data.Folder, "accounts", "aaaaaaaa00000001"));
        var second = Directory.CreateDirectory(Path.Combine(_data.Folder, "accounts", "aaaaaaaa00000002"));
        _account.SignIn(null);

        _subject.KeyOf("aaaaaaaa").Should().BeNull("two accounts that the id cannot tell apart are named by none, so that the wrong one is never forgotten");
        _subject.Forget("aaaaaaaa").Should().Be(ForgetResult.Unknown);
        first.Exists.Should().BeTrue();
        second.Exists.Should().BeTrue();
    }

    [Fact]
    public void should_not_forget_an_account_that_a_pass_has_taken_even_when_nobody_is_signed_in_any_more()
    {
        Read(Jane, "c1");
        _account.SignIn(null);
        _account.Forget();

        using (_account.Use(AccountKey.Of(Jane)))
        {
            _subject.Forget(AccountService.IdOf(AccountKey.Of(Jane))).Should().Be(ForgetResult.Active);
        }

        _workspaces.Exists(AccountKey.Of(Jane)).Should().BeTrue();
        _subject.Forget(AccountService.IdOf(AccountKey.Of(Jane))).Should().Be(ForgetResult.Forgotten, "once the pass is done it can be forgotten");
    }
}
