using System.Net;
using Geminarr.Core.Accounts;
using Geminarr.Core.Link;
using Geminarr.Core.Pipeline;
using Geminarr.Core.Test.Access;
using Geminarr.Core.Test.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Geminarr.Core.Test.Link;

/// <summary>The link of two accounts that are signed in in turn: each has its machine, its key and its pairing, and the one never sees the other's (SPEC §8).</summary>
public sealed class AccountsLinkFixture : IDisposable
{
    private const string Url = "https://mnemarr.homelab.example";
    private const string Jane = "jane.doe@example.org";
    private const string John = "john.roe@example.org";

    private readonly TestDatabase _data = new();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 20, 9, 0, 0, TimeSpan.Zero));
    private readonly SwitchableAccount _account = new();
    private readonly FakeHttp _http = new(_ => FakeHttp.Status(HttpStatusCode.NotFound));
    private readonly LinkService _subject;
    private readonly KeyStore _keys;

    public AccountsLinkFixture()
    {
        var workspaces = new AccountWorkspaces(Options.Of(_data.Settings));
        var database = new AccountDatabase(_account, workspaces);
        _keys = new KeyStore(Options.Of(new MnemarrOptions()), _account, workspaces);
        _subject = new LinkService(
            Options.Of(new MnemarrOptions()),
            new LinkStores(new DestinationStore(database), _keys, new OutboxStore(database), _account, workspaces),
            new MnemarrClient(_http),
            _clock,
            NullLogger<LinkService>.Instance);
    }

    public void Dispose() => _data.Dispose();

    /// <summary>Mnemarr as it answers a pairing: a code at the start, the key and the name of the machine once a person approved.</summary>
    private static Func<SeenRequest, HttpResponseMessage> Mnemarr(string pollState = "pending", string? key = null, string machine = "whatever") => request =>
        request.Address.EndsWith("/pair/poll", StringComparison.Ordinal)
            ? FakeHttp.Json($$"""{"state":"{{pollState}}","key":{{(key is null ? "null" : $"\"{key}\"")}},"machine":"{{machine}}","kind":"satellite"}""")
            : FakeHttp.Json("""{"userCode":"ABCD-EFGH","deviceCode":"device-secret","verificationUrl":"https://mnemarr.homelab.example/link?code=ABCD-EFGH","expiresInSeconds":600,"intervalSeconds":3}""", HttpStatusCode.Created);

    private async Task Pair(string key)
    {
        _http.Answer = Mnemarr();
        await _subject.StartPairingAsync(TestContext.Current.CancellationToken);
        _http.Answer = Mnemarr("approved", key, _subject.DerivedMachine()!);
        await _subject.PollPairingAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task should_ask_mnemarr_for_a_machine_named_from_the_address_of_the_account()
    {
        _account.SignIn(Jane);
        _subject.SetAddress(Url);
        _http.Answer = Mnemarr();

        await _subject.StartPairingAsync(TestContext.Current.CancellationToken);

        _http.Requests.Single().Body.Should().Contain("geminarr-jane-doe");
        _subject.Status().Machine.Should().Be("geminarr-jane-doe");
    }

    [Fact]
    public async Task should_keep_the_key_and_the_link_of_each_account_apart()
    {
        _account.SignIn(Jane);
        _subject.SetAddress(Url);
        await Pair("mnm_jane");

        _account.SignIn(John);

        _subject.Status().Linked.Should().BeFalse("the other account has not been paired: its key is not the first one's");
        _keys.Read().Should().BeNull();

        _subject.SetAddress(Url);
        await Pair("mnm_john");
        _keys.Read().Should().Be("mnm_john");
        _account.SignIn(Jane);
        _keys.Read().Should().Be("mnm_jane");
        _subject.Status().Machine.Should().Be("geminarr-jane-doe");
    }

    [Fact]
    public async Task should_not_give_two_accounts_the_same_machine_name()
    {
        _account.SignIn("jane.doe@example.org");
        _subject.SetAddress(Url);
        await Pair("mnm_one");

        _account.SignIn("jane.doe@example.com");

        var key = AccountKey.Of("jane.doe@example.com");
        _subject.DerivedMachine().Should().Be("geminarr-jane-doe-" + key[..4], "another account has geminarr-jane-doe");
    }

    [Fact]
    public async Task should_refuse_to_pair_when_no_account_is_signed_in_and_none_was()
    {
        _subject.Status().Linked.Should().BeFalse();

        var start = () => _subject.StartPairingAsync(TestContext.Current.CancellationToken);

        await start.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task should_not_let_a_pairing_that_an_account_started_be_answered_for_another()
    {
        _account.SignIn(Jane);
        _subject.SetAddress(Url);
        _http.Answer = Mnemarr();
        await _subject.StartPairingAsync(TestContext.Current.CancellationToken);

        _account.SignIn(John);
        _http.Answer = Mnemarr("approved", "mnm_stolen", "whatever");

        _subject.Pairing().State.Should().Be(PairingStates.None);
        (await _subject.PollPairingAsync(TestContext.Current.CancellationToken)).State.Should().Be(PairingStates.None);
        _keys.Read().Should().BeNull("the key of a pairing is never written to the workspace of another account");
    }

    [Fact]
    public async Task should_unlink_one_account_and_leave_the_other_linked()
    {
        _account.SignIn(Jane);
        _subject.SetAddress(Url);
        await Pair("mnm_jane");
        _account.SignIn(John);
        _subject.SetAddress(Url);
        await Pair("mnm_john");

        _subject.Unlink();

        _subject.Status().Linked.Should().BeFalse();
        _account.SignIn(Jane);
        _subject.Status().Linked.Should().BeTrue();
        _keys.Read().Should().Be("mnm_jane");
    }

    [Fact]
    public async Task should_keep_the_machine_of_an_account_after_it_signed_out_because_the_session_expired()
    {
        _account.SignIn(Jane);
        _subject.SetAddress(Url);
        await Pair("mnm_jane");

        _account.SignIn(null);

        _account.Current.Should().Be(AccountKey.Of(Jane));
        _subject.Status().Linked.Should().BeTrue("an expired session is still told to Mnemarr by the machine of its account");
        _subject.Status().Machine.Should().Be("geminarr-jane-doe");
    }
}
