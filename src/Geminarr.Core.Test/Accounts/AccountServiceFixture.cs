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
        var link = new LinkService(
            Options.Of(new MnemarrOptions()),
            new LinkStores(new DestinationStore(_database), new KeyStore(Options.Of(new MnemarrOptions()), _account, _workspaces), new OutboxStore(_database), _account, _workspaces),
            new MnemarrClient(new FakeHttp(_ => FakeHttp.Status(HttpStatusCode.NotFound))),
            new FakeTimeProvider(Now),
            NullLogger<LinkService>.Instance);
        _subject = new AccountService(_account, _workspaces, link);
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
}
