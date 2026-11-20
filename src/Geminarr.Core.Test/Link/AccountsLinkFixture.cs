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

    /// <summary>A link that enrols with the code in the file: a deployment as code.</summary>
    private (LinkService Subject, KeyStore Keys) EnrolmentSubject(string codeFile)
    {
        var options = new MnemarrOptions { Url = Url, EnrolmentCodeFile = codeFile };
        var workspaces = new AccountWorkspaces(Options.Of(_data.Settings));
        var database = new AccountDatabase(_account, workspaces);
        var keys = new KeyStore(Options.Of(options), _account, workspaces);
        var subject = new LinkService(Options.Of(options), new LinkStores(new DestinationStore(database), keys, new OutboxStore(database), _account, workspaces), new MnemarrClient(_http), _clock, NullLogger<LinkService>.Instance);
        return (subject, keys);
    }

    [Fact]
    public async Task should_enrol_with_the_code_of_the_deployment_only_once_an_account_is_signed_in()
    {
        var file = Path.Combine(_data.Folder, "code");
        await File.WriteAllTextAsync(file, "ABCD-1234\n", TestContext.Current.CancellationToken);
        var (subject, _) = EnrolmentSubject(file);
        _http.Answer = _ => FakeHttp.Json("""{"key":"mnm_enrolled","machine":"gemini","kind":"satellite"}""");

        // Nobody is signed in: there is no workspace for the key, so nothing is asked and the next wake tries again.
        (await subject.EnrolFromCodeFileAsync(TestContext.Current.CancellationToken)).Should().BeFalse();
        _http.Requests.Should().BeEmpty();

        _account.SignIn(Jane);
        (await subject.EnrolFromCodeFileAsync(TestContext.Current.CancellationToken)).Should().BeTrue();
        subject.Status().Should().Match<DestinationStatus>(status => status.Linked && status.Machine == "gemini");
    }

    [Fact]
    public async Task should_write_the_key_for_the_account_that_asked_when_another_signs_in_while_mnemarr_answers()
    {
        _account.SignIn(Jane);
        _subject.SetAddress(Url);
        _http.Answer = Mnemarr();
        await _subject.StartPairingAsync(TestContext.Current.CancellationToken);
        var approved = Mnemarr("approved", "mnm_jane", _subject.DerivedMachine()!);
        // The other account signs in while the answer is on its way.
        _http.Answer = request =>
        {
            _account.SignIn(John);
            return approved(request);
        };

        await _subject.PollPairingAsync(TestContext.Current.CancellationToken);

        _account.SignIn(John);
        _keys.Read().Should().BeNull("the key of Jane was not written to the workspace of John");
        _account.SignIn(Jane);
        _keys.Read().Should().Be("mnm_jane");
    }

    [Fact]
    public async Task should_write_the_key_of_an_enrolment_for_the_account_that_sent_the_code()
    {
        var file = Path.Combine(_data.Folder, "code");
        await File.WriteAllTextAsync(file, "ABCD-1234\n", TestContext.Current.CancellationToken);
        var (subject, keys) = EnrolmentSubject(file);
        _account.SignIn(Jane);
        _http.Answer = _ =>
        {
            _account.SignIn(John);
            return FakeHttp.Json("""{"key":"mnm_enrolled","machine":"gemini","kind":"satellite"}""");
        };

        await subject.EnrolFromCodeFileAsync(TestContext.Current.CancellationToken);

        _account.SignIn(John);
        keys.Read().Should().BeNull();
        _account.SignIn(Jane);
        keys.Read().Should().Be("mnm_enrolled");
    }

    [Fact]
    public async Task should_give_a_new_account_the_address_of_mnemarr_that_another_account_has()
    {
        _account.SignIn(Jane);
        _subject.SetAddress(Url);

        _account.SignIn(John);

        _subject.Status().Url.Should().Be(Url, "the address is one for every account: it is typed once");
        _http.Answer = Mnemarr();
        await _subject.StartPairingAsync(TestContext.Current.CancellationToken);
        _http.Requests.Single().Address.Should().StartWith(Url);
    }

    [Fact]
    public void should_give_a_new_account_the_address_of_the_first_account_in_the_order_of_the_keys_when_two_differ()
    {
        _account.SignIn(Jane);
        _subject.SetAddress("https://one.homelab.example");
        _account.SignIn(John);
        _subject.SetAddress("https://two.homelab.example");
        var third = "kim.lee@example.org";
        var first = new[] { Jane, John }.OrderBy(AccountKey.Of, StringComparer.Ordinal).First();

        _account.SignIn(third);

        _subject.Status().Url.Should().Be(first == Jane ? "https://one.homelab.example" : "https://two.homelab.example", "the rule is the order of the keys of the accounts that have an address");
    }

    [Fact]
    public async Task should_say_that_no_account_is_signed_in_with_a_type_of_its_own()
    {
        var setAddress = () => _subject.SetAddress(Url);
        var start = () => _subject.StartPairingAsync(TestContext.Current.CancellationToken);

        setAddress.Should().Throw<NoAccountException>();
        await start.Should().ThrowAsync<NoAccountException>();
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
    public async Task should_tell_mnemarr_which_computer_an_account_is_so_that_another_account_cannot_take_its_machine()
    {
        _account.SignIn(Jane);
        _subject.SetAddress(Url);
        _http.Answer = Mnemarr();
        await _subject.StartPairingAsync(TestContext.Current.CancellationToken);
        _account.SignIn(John);
        await _subject.StartPairingAsync(TestContext.Current.CancellationToken);

        var ids = _http.Requests.Select(request => System.Text.RegularExpressions.Regex.Match(request.Body, "\"computerId\":\"([0-9a-f]{64})\"").Groups[1].Value).ToList();
        ids.Should().HaveCount(2).And.OnlyContain(id => id.Length == 64);
        ids[0].Should().NotBe(ids[1]);
        ids[0].Should().Be(AccountKey.ComputerId(AccountKey.Of(Jane)), "it is the same for the same account, whichever Geminarr asks");
        ids[0].Should().NotContain(AccountKey.Of(Jane), "it carries neither the address nor the key");
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
