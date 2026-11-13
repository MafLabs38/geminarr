using System.Net;
using Geminarr.Core.Link;
using Geminarr.Core.Pipeline;
using Geminarr.Core.Test.Access;
using Geminarr.Core.Test.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Geminarr.Core.Test.Link;

public sealed class LinkServiceFixture : IDisposable
{
    private const string Url = "https://mnemarr.homelab.example";

    private readonly TestDatabase _workspace = new();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 13, 9, 0, 0, TimeSpan.Zero));
    private readonly FakeKeys _keys = new();
    private readonly DestinationStore _store;
    private readonly FakeHttp _http = new(_ => FakeHttp.Status(HttpStatusCode.NotFound));

    public LinkServiceFixture()
    {
        _store = new DestinationStore(_workspace.Database);
    }

    public void Dispose() => _workspace.Dispose();

    private LinkService Subject(MnemarrOptions? options = null) => new(Options.Of(options ?? new MnemarrOptions()), _store, _keys, new MnemarrClient(_http), _clock, NullLogger<LinkService>.Instance);

    private static Func<SeenRequest, HttpResponseMessage> Mnemarr(string pollState = "pending", string? key = null, string machine = "gemini-2") => request =>
        request.Address.EndsWith("/pair/poll", StringComparison.Ordinal)
            ? FakeHttp.Json($$"""{"state":"{{pollState}}","key":{{(key is null ? "null" : $"\"{key}\"")}},"machine":"{{machine}}","kind":"satellite"}""")
            : FakeHttp.Json("""{"userCode":"ABCD-EFGH","deviceCode":"device-secret","verificationUrl":"https://mnemarr.homelab.example/link?code=ABCD-EFGH","expiresInSeconds":600,"intervalSeconds":3}""", HttpStatusCode.Created);

    [Fact]
    public void should_say_nothing_is_linked_before_an_address_and_a_key_exist()
    {
        var status = Subject().Status();

        status.Linked.Should().BeFalse();
        status.Problem.Should().Be(DestinationProblems.NotLinked);
        status.Machine.Should().Be("gemini");
        status.Url.Should().BeNull();
    }

    [Fact]
    public void should_take_the_address_from_the_page_and_link_when_a_key_is_there()
    {
        var subject = Subject();
        subject.SetAddress("https://mnemarr.homelab.example/");
        _keys.Key = "mnm_key";

        var status = subject.Status();

        status.Url.Should().Be(Url);
        status.Linked.Should().BeTrue();
        status.AddressFromSettings.Should().BeFalse();
        status.Problem.Should().BeNull();
        subject.Resolve().Should().Be(new ResolvedDestination(Url, "gemini", "mnm_key"));
    }

    [Fact]
    public void should_prefer_the_address_of_the_settings_and_refuse_to_change_it_from_the_page()
    {
        var subject = Subject(new MnemarrOptions { Url = Url, Machine = "gemini-3" });
        _keys.Key = "k";

        subject.Status().AddressFromSettings.Should().BeTrue();
        subject.Resolve().Machine.Should().Be("gemini-3");
        var change = () => subject.SetAddress("https://elsewhere.homelab.example");
        change.Should().Throw<InvalidOperationException>().Which.Message.Should().Contain("deployment");
    }

    [Fact]
    public void should_refuse_a_new_address_while_a_key_is_held_and_an_address_that_is_not_one()
    {
        var subject = Subject();
        subject.SetAddress(Url);
        _keys.Key = "k";

        var change = () => subject.SetAddress("https://other.homelab.example");
        change.Should().Throw<InvalidOperationException>().Which.Message.Should().Contain("Unlink");

        _keys.Key = null;
        var bad = () => subject.SetAddress("not an address");
        bad.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void should_say_it_cannot_resolve_a_destination_without_an_address_or_a_key_or_with_a_plain_http_one()
    {
        var subject = Subject();
        var nowhere = () => subject.Resolve();
        nowhere.Should().Throw<SinkException>().Which.Failure.Should().Be(SinkFailure.NotLinked);

        subject.SetAddress("http://mnemarr.homelab.example");
        _keys.Key = "k";
        var plain = () => subject.Resolve();
        plain.Should().Throw<SinkException>().Which.Failure.Should().Be(SinkFailure.InsecureAddress);
        subject.Status().Problem.Should().Be(DestinationProblems.InsecureAddress);
    }

    [Fact]
    public void should_allow_plain_http_in_a_sandbox_and_to_the_loopback()
    {
        var sandbox = Subject(new MnemarrOptions { AllowInsecureHttp = true });
        sandbox.SetAddress("http://mnemarr.homelab.example");
        _keys.Key = "k";
        sandbox.Resolve().Url.Should().Be("http://mnemarr.homelab.example");

        _keys.Key = null;
        _store.SaveAddress("http://localhost:8383");
        _keys.Key = "k";
        Subject().Resolve().Url.Should().Be("http://localhost:8383");
    }

    [Fact]
    public void should_keep_the_problem_the_last_talk_left_until_the_next_one_works()
    {
        var subject = Subject();
        subject.SetAddress(Url);
        _keys.Key = "k";

        subject.RecordTalk(DestinationProblems.Refused);
        subject.Status().Problem.Should().Be(DestinationProblems.Refused);
        subject.Status().LastHeartbeatAt.Should().Be(_clock.GetUtcNow());

        subject.RecordTalk(null);
        subject.Status().Problem.Should().BeNull();
    }

    [Fact]
    public void should_keep_a_row_for_an_address_that_only_the_settings_give()
    {
        var subject = Subject(new MnemarrOptions { Url = Url });

        subject.RecordTalk(null);

        _store.Get()!.Url.Should().Be(Url);
        _store.Get()!.LastHeartbeatAt.Should().Be(_clock.GetUtcNow());
    }

    [Fact]
    public async Task should_pair_by_asking_and_keep_the_key_once_a_person_has_approved()
    {
        _http.Answer = Mnemarr();
        var subject = Subject();
        subject.SetAddress(Url);

        var started = await subject.StartPairingAsync(TestContext.Current.CancellationToken);

        started.State.Should().Be(PairingStates.Pending);
        started.UserCode.Should().Be("ABCD-EFGH");
        started.VerificationUrl.Should().Be("https://mnemarr.homelab.example/link?code=ABCD-EFGH");
        started.ExpiresAt.Should().Be(_clock.GetUtcNow().AddSeconds(600));
        subject.Pairing().State.Should().Be(PairingStates.Pending);
        (await subject.PollPairingAsync(TestContext.Current.CancellationToken)).State.Should().Be(PairingStates.Pending);
        _keys.Key.Should().BeNull();

        _http.Answer = Mnemarr("approved", "mnm_new", "gemini-2");
        var approved = await subject.PollPairingAsync(TestContext.Current.CancellationToken);

        approved.State.Should().Be(PairingStates.Approved);
        _keys.Key.Should().Be("mnm_new");
        var status = subject.Status();
        status.Linked.Should().BeTrue();
        status.Machine.Should().Be("gemini-2", "the person who approved named the machine");
        subject.Pairing().State.Should().Be(PairingStates.None);
    }

    [Fact]
    public async Task should_never_give_the_secret_of_the_pairing_to_the_page()
    {
        _http.Answer = Mnemarr();
        var subject = Subject();
        subject.SetAddress(Url);

        var started = await subject.StartPairingAsync(TestContext.Current.CancellationToken);

        System.Text.Json.JsonSerializer.Serialize(started).Should().NotContain("device-secret");
        System.Text.Json.JsonSerializer.Serialize(subject.Pairing()).Should().NotContain("device-secret");
    }

    [Theory]
    [InlineData("rejected")]
    [InlineData("expired")]
    public async Task should_report_a_pairing_that_was_refused_or_has_expired_and_forget_it(string state)
    {
        _http.Answer = Mnemarr(state);
        var subject = Subject();
        subject.SetAddress(Url);
        await subject.StartPairingAsync(TestContext.Current.CancellationToken);

        var polled = await subject.PollPairingAsync(TestContext.Current.CancellationToken);

        polled.State.Should().Be(state);
        subject.Pairing().State.Should().Be(PairingStates.None);
        _keys.Key.Should().BeNull();
    }

    [Fact]
    public async Task should_let_a_pairing_expire_by_the_clock_without_asking_mnemarr()
    {
        _http.Answer = Mnemarr();
        var subject = Subject();
        subject.SetAddress(Url);
        await subject.StartPairingAsync(TestContext.Current.CancellationToken);
        var calls = _http.Requests.Count;
        _clock.Advance(TimeSpan.FromMinutes(11));

        (await subject.PollPairingAsync(TestContext.Current.CancellationToken)).State.Should().Be(PairingStates.Expired);

        _http.Requests.Count.Should().Be(calls);
        subject.Pairing().State.Should().Be(PairingStates.None);
    }

    [Fact]
    public async Task should_say_there_is_nothing_to_poll_and_allow_giving_a_pairing_up()
    {
        var subject = Subject();
        (await subject.PollPairingAsync(TestContext.Current.CancellationToken)).State.Should().Be(PairingStates.None);

        _http.Answer = Mnemarr();
        subject.SetAddress(Url);
        await subject.StartPairingAsync(TestContext.Current.CancellationToken);
        subject.CancelPairing();

        subject.Pairing().State.Should().Be(PairingStates.None);
    }

    [Fact]
    public async Task should_not_start_a_pairing_without_an_address_or_to_a_plain_http_one()
    {
        var subject = Subject();
        var nowhere = () => subject.StartPairingAsync(TestContext.Current.CancellationToken);
        (await nowhere.Should().ThrowAsync<SinkException>()).Which.Failure.Should().Be(SinkFailure.NotLinked);

        subject.SetAddress("http://mnemarr.homelab.example");
        var plain = () => subject.StartPairingAsync(TestContext.Current.CancellationToken);
        (await plain.Should().ThrowAsync<SinkException>()).Which.Failure.Should().Be(SinkFailure.InsecureAddress);
    }

    [Fact]
    public async Task should_only_show_a_page_to_approve_that_is_an_http_address()
    {
        _http.Answer = (_ => FakeHttp.Json("""{"userCode":"A","deviceCode":"d","verificationUrl":"javascript:alert(1)","expiresInSeconds":600,"intervalSeconds":3}""", HttpStatusCode.Created));
        var subject = Subject();
        subject.SetAddress(Url);

        var started = await subject.StartPairingAsync(TestContext.Current.CancellationToken);

        started.VerificationUrl.Should().BeNull();
        started.UserCode.Should().Be("A");
    }

    [Fact]
    public async Task should_enrol_with_a_code_file_when_there_is_no_key_and_use_a_key_that_was_given_as_it_is()
    {
        var file = Path.Combine(_workspace.Folder, "code");
        await File.WriteAllTextAsync(file, "ABCD-1234\n", TestContext.Current.CancellationToken);
        _http.Answer = (_ => FakeHttp.Json("""{"key":"mnm_enrolled","machine":"gemini","kind":"satellite"}"""));
        var subject = Subject(new MnemarrOptions { Url = Url, EnrolmentCodeFile = file });

        await subject.EnrolFromCodeFileAsync(TestContext.Current.CancellationToken);

        _keys.Key.Should().Be("mnm_enrolled");
        _http.Requests.Single().Body.Should().Contain("ABCD-1234");
        subject.Status().Linked.Should().BeTrue();

        await subject.EnrolFromCodeFileAsync(TestContext.Current.CancellationToken);
        _http.Requests.Should().ContainSingle("a key that is held is not exchanged again");
    }

    [Fact]
    public async Task should_not_enrol_without_a_code_an_address_or_a_safe_address()
    {
        var calls = 0;
        _http.Answer = (_ =>
        {
            calls++;
            return FakeHttp.Json("""{"key":"k","machine":"gemini"}""");
        });
        var missing = Path.Combine(_workspace.Folder, "missing");
        var empty = Path.Combine(_workspace.Folder, "empty");
        await File.WriteAllTextAsync(empty, "\n", TestContext.Current.CancellationToken);

        await Subject().EnrolFromCodeFileAsync(TestContext.Current.CancellationToken);
        await Subject(new MnemarrOptions { EnrolmentCodeFile = empty }).EnrolFromCodeFileAsync(TestContext.Current.CancellationToken);
        await Subject(new MnemarrOptions { Url = Url, EnrolmentCodeFile = missing }).EnrolFromCodeFileAsync(TestContext.Current.CancellationToken);
        await Subject(new MnemarrOptions { Url = Url, EnrolmentCodeFile = empty }).EnrolFromCodeFileAsync(TestContext.Current.CancellationToken);
        await Subject(new MnemarrOptions { Url = "http://mnemarr.homelab.example", EnrolmentCodeFile = empty }).EnrolFromCodeFileAsync(TestContext.Current.CancellationToken);

        calls.Should().Be(0);
        _keys.Key.Should().BeNull();
    }

    [Fact]
    public async Task should_unlink_by_forgetting_the_key_the_machine_and_any_pairing()
    {
        _http.Answer = Mnemarr();
        var subject = Subject();
        subject.SetAddress(Url);
        await subject.StartPairingAsync(TestContext.Current.CancellationToken);
        _keys.Key = "k";

        subject.Unlink();

        _keys.Key.Should().BeNull();
        _store.Get().Should().BeNull();
        subject.Pairing().State.Should().Be(PairingStates.None);
        subject.Status().Linked.Should().BeFalse();
    }

    [Fact]
    public async Task should_send_through_the_sink_to_the_linked_mnemarr_and_say_when_there_is_none()
    {
        _http.Answer = (request => request.Address.Contains("positions", StringComparison.Ordinal) ? FakeHttp.Json("""[{"session":"gemini-a","lines":2}]""") : FakeHttp.Json("""{"next":3,"accepted":1}"""));
        var subject = Subject(new MnemarrOptions { Url = Url });
        var sink = new MnemarrSink(subject, new MnemarrClient(_http));

        var lonely = () => sink.PositionsAsync(TestContext.Current.CancellationToken);
        (await lonely.Should().ThrowAsync<SinkException>()).Which.Failure.Should().Be(SinkFailure.NotLinked);

        _keys.Key = "mnm_key";
        (await sink.PositionsAsync(TestContext.Current.CancellationToken)).Lines["gemini-a"].Should().Be(2);
        (await sink.SendAsync("gemini-a", 2, ["x"], TestContext.Current.CancellationToken)).Should().Be(new SinkResult(3, 1));
        await sink.HeartbeatAsync(new Heartbeat("v", 0, null, null), TestContext.Current.CancellationToken);
        _http.Requests.Select(request => request.Address).Should().Equal($"{Url}/api/v1/ingest/positions?machine=gemini", $"{Url}/api/v1/ingest/conversations", $"{Url}/api/v1/ingest/heartbeat");
    }
}
