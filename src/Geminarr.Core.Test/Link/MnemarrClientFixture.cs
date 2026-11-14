using System.Net;
using System.Text.Json;
using Geminarr.Core.Link;
using Geminarr.Core.Pipeline;
using Geminarr.Core.Test.Support;

namespace Geminarr.Core.Test.Link;

public sealed class MnemarrClientFixture
{
    private const string Url = "https://mnemarr.homelab.example";

    [Fact]
    public async Task should_ask_for_the_positions_of_its_machine_with_its_key_and_read_them()
    {
        var http = new FakeHttp(_ => FakeHttp.Json("""[{"session":"gemini-a","lines":5},{"session":"gemini-b","lines":1},{"session":"gemini-a","lines":7}]"""));

        var positions = await new MnemarrClient(http).PositionsAsync(Url, "mnm_key", "gemini 2", TestContext.Current.CancellationToken);

        positions.Lines.Should().Equal(new Dictionary<string, long> { ["gemini-a"] = 7, ["gemini-b"] = 1 });
        var request = http.Requests.Single();
        request.Method.Should().Be(HttpMethod.Get);
        request.Address.Should().Be($"{Url}/api/v1/ingest/positions?machine=gemini%202");
        request.Headers["Authorization"].Should().Be("Bearer mnm_key");
        request.Headers.Should().NotContainKey("X-Mnemarr-Request");
    }

    [Fact]
    public async Task should_say_in_the_heartbeat_that_the_machine_is_paused()
    {
        var http = new FakeHttp(_ => FakeHttp.Status(System.Net.HttpStatusCode.NoContent));

        await new MnemarrClient(http).HeartbeatAsync(Url, "k", new Heartbeat("0.1.0", 0, null, null, Paused: true), TestContext.Current.CancellationToken);

        http.Requests.Single().Body.Should().Contain("\"paused\":true");
    }

    [Fact]
    public void should_never_print_a_key_or_a_device_code_when_a_record_is_printed()
    {
        new[] { new Enrolled("mnm_secret_key", "gemini").ToString(), new PairPolled("approved", "mnm_secret_key", "gemini").ToString(), new PairStarted("ABCD", "device_secret", "https://x.example", 600, 3).ToString() }
            .Should().OnlyContain(text => !text.Contains("secret") && !text.Contains("ABCD"));
    }

    [Fact]
    public async Task should_fail_as_unreachable_when_the_answer_stops_after_its_headers()
    {
        var http = new FakeHttp(_ => new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StreamContent(new HangingStream()) });

        var call = () => new MnemarrClient(http, TimeSpan.FromMilliseconds(100)).PositionsAsync(Url, "mnm_key", "gemini", TestContext.Current.CancellationToken);

        (await call.Should().ThrowAsync<SinkException>()).Which.Failure.Should().Be(SinkFailure.Unreachable);
    }

    [Fact]
    public async Task should_fail_as_unreachable_when_the_connection_breaks_while_the_answer_is_read()
    {
        var http = new FakeHttp(_ => new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StreamContent(new BrokenStream()) });

        var call = () => new MnemarrClient(http).PositionsAsync(Url, "mnm_key", "gemini", TestContext.Current.CancellationToken);

        (await call.Should().ThrowAsync<SinkException>()).Which.Failure.Should().Be(SinkFailure.Unreachable);
    }

    [Fact]
    public async Task should_send_lines_in_the_format_chat_turns_as_the_application_gemini()
    {
        var http = new FakeHttp(_ => FakeHttp.Json("""{"next":3,"accepted":2}"""));

        var result = await new MnemarrClient(http).SendAsync(Url, "mnm_key", "gemini", "gemini-a", 1, ["l1", "l2"], TestContext.Current.CancellationToken);

        result.Should().Be(new SinkResult(3, 2));
        var request = http.Requests.Single();
        request.Address.Should().Be($"{Url}/api/v1/ingest/conversations");
        request.Headers["X-Mnemarr-Request"].Should().Be("1");
        using var body = JsonDocument.Parse(request.Body);
        body.RootElement.GetProperty("format").GetString().Should().Be("chat-turns");
        body.RootElement.GetProperty("machine").GetString().Should().Be("gemini");
        body.RootElement.GetProperty("application").GetString().Should().Be("gemini");
        body.RootElement.GetProperty("session").GetString().Should().Be("gemini-a");
        body.RootElement.GetProperty("from").GetInt64().Should().Be(1);
        body.RootElement.GetProperty("lines").EnumerateArray().Select(line => line.GetString()).Should().Equal("l1", "l2");
    }

    [Fact]
    public async Task should_say_where_it_stands_when_mnemarr_gives_no_body()
    {
        var http = new FakeHttp(_ => FakeHttp.Status(HttpStatusCode.NoContent));

        var result = await new MnemarrClient(http).SendAsync(Url, "k", "gemini", "gemini-a", 4, ["a", "b"], TestContext.Current.CancellationToken);

        result.Should().Be(new SinkResult(6, 2));
    }

    [Fact]
    public async Task should_send_the_heartbeat_with_what_it_is_asking_of_the_person_and_nothing_when_nothing_is_wrong()
    {
        var http = new FakeHttp(_ => FakeHttp.Status(HttpStatusCode.NoContent));
        var client = new MnemarrClient(http);

        await client.HeartbeatAsync(Url, "k", new Heartbeat("0.1.0", 12, "The session has expired.", new Attention("needs-login", "https://geminarr.homelab.example/")), TestContext.Current.CancellationToken);
        await client.HeartbeatAsync(Url, "k", new Heartbeat("0.1.0", 0, null, null), TestContext.Current.CancellationToken);

        using var first = JsonDocument.Parse(http.Requests[0].Body);
        first.RootElement.GetProperty("version").GetString().Should().Be("0.1.0");
        first.RootElement.GetProperty("pendingLines").GetInt64().Should().Be(12);
        first.RootElement.GetProperty("error").GetString().Should().Be("The session has expired.");
        first.RootElement.GetProperty("paused").GetBoolean().Should().BeFalse();
        first.RootElement.GetProperty("attention").GetProperty("code").GetString().Should().Be("needs-login");
        first.RootElement.GetProperty("attention").GetProperty("url").GetString().Should().Be("https://geminarr.homelab.example/");
        using var second = JsonDocument.Parse(http.Requests[1].Body);
        second.RootElement.GetProperty("attention").ValueKind.Should().Be(JsonValueKind.Null);
        http.Requests[0].Address.Should().Be($"{Url}/api/v1/ingest/heartbeat");
    }

    [Fact]
    public async Task should_exchange_a_code_for_a_key_with_no_key_of_its_own()
    {
        var http = new FakeHttp(_ => FakeHttp.Json("""{"key":"mnm_new","machine":"gemini","kind":"satellite"}"""));

        var enrolled = await new MnemarrClient(http).EnrolAsync(Url, "ABCD-1234", "gemini", TestContext.Current.CancellationToken);

        enrolled.Should().Be(new Enrolled("mnm_new", "gemini"));
        http.Requests.Single().Headers.Should().NotContainKey("Authorization");
        http.Requests.Single().Headers["X-Mnemarr-Request"].Should().Be("1");
        http.Requests.Single().Address.Should().Be($"{Url}/api/v1/ingest/enrol");
    }

    [Fact]
    public async Task should_start_and_poll_a_pairing()
    {
        var http = new FakeHttp(request => request.Address.EndsWith("/poll", StringComparison.Ordinal)
            ? FakeHttp.Json("""{"state":"approved","key":"mnm_new","machine":"gemini-2","kind":"satellite"}""")
            : FakeHttp.Json("""{"userCode":"ABCD-EFGH","deviceCode":"secret-device","verificationUrl":"https://mnemarr.homelab.example/link?code=ABCD-EFGH","expiresInSeconds":600,"intervalSeconds":3}""", HttpStatusCode.Created));
        var client = new MnemarrClient(http);

        var started = await client.PairStartAsync(Url, "gemini", TestContext.Current.CancellationToken);
        var polled = await client.PairPollAsync(Url, started.DeviceCode, TestContext.Current.CancellationToken);

        started.UserCode.Should().Be("ABCD-EFGH");
        polled.Should().Be(new PairPolled("approved", "mnm_new", "gemini-2"));
        http.Requests[0].Body.Should().Contain("\"hostname\":\"gemini\"");
        http.Requests[1].Body.Should().Contain("secret-device");
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, SinkFailure.Refused)]
    [InlineData(HttpStatusCode.Forbidden, SinkFailure.Refused)]
    [InlineData(HttpStatusCode.BadRequest, SinkFailure.Rejected)]
    [InlineData(HttpStatusCode.RequestEntityTooLarge, SinkFailure.Rejected)]
    [InlineData(HttpStatusCode.UnprocessableEntity, SinkFailure.Rejected)]
    [InlineData(HttpStatusCode.NotFound, SinkFailure.Rejected)]
    [InlineData(HttpStatusCode.Conflict, SinkFailure.Rejected)]
    [InlineData(HttpStatusCode.RequestTimeout, SinkFailure.Unreachable)]
    [InlineData(HttpStatusCode.InternalServerError, SinkFailure.Unreachable)]
    [InlineData(HttpStatusCode.Redirect, SinkFailure.Unreachable)]
    public async Task should_say_which_kind_of_failure_it_was_from_the_status(HttpStatusCode status, SinkFailure expected)
    {
        var http = new FakeHttp(_ => FakeHttp.Status(status));

        var send = () => new MnemarrClient(http).SendAsync(Url, "k", "gemini", "gemini-a", 0, ["x"], TestContext.Current.CancellationToken);

        (await send.Should().ThrowAsync<SinkException>()).Which.Failure.Should().Be(expected);
    }

    [Fact]
    public async Task should_say_unreachable_for_a_network_error_and_never_repeat_the_address_or_the_content()
    {
        var http = new FakeHttp(_ => throw new HttpRequestException("connection refused to https://mnemarr.homelab.example with a secret"));

        var send = () => new MnemarrClient(http).SendAsync(Url, "k", "gemini", "gemini-a", 0, ["the text of a turn"], TestContext.Current.CancellationToken);

        var failure = (await send.Should().ThrowAsync<SinkException>()).Which;
        failure.Failure.Should().Be(SinkFailure.Unreachable);
        failure.Message.Should().NotContain("mnemarr.homelab").And.NotContain("secret").And.NotContain("the text of a turn");
    }

    [Fact]
    public async Task should_say_unreachable_for_an_answer_that_is_not_json()
    {
        var http = new FakeHttp(_ => FakeHttp.Json("<html>a proxy page</html>"));

        var read = () => new MnemarrClient(http).PositionsAsync(Url, "k", "gemini", TestContext.Current.CancellationToken);

        (await read.Should().ThrowAsync<SinkException>()).Which.Failure.Should().Be(SinkFailure.Unreachable);
    }

    [Fact]
    public async Task should_refuse_an_enrolment_that_gives_no_key_and_a_pairing_that_gives_no_code()
    {
        var empty = new FakeHttp(_ => FakeHttp.Json("{}"));
        var client = new MnemarrClient(empty);

        (await ((Func<Task>)(() => client.EnrolAsync(Url, "c", "gemini", TestContext.Current.CancellationToken))).Should().ThrowAsync<SinkException>()).Which.Failure.Should().Be(SinkFailure.Rejected);
        (await ((Func<Task>)(() => client.PairStartAsync(Url, "gemini", TestContext.Current.CancellationToken))).Should().ThrowAsync<SinkException>()).Which.Failure.Should().Be(SinkFailure.Rejected);
        (await ((Func<Task>)(() => client.PairPollAsync(Url, "d", TestContext.Current.CancellationToken))).Should().ThrowAsync<SinkException>()).Which.Failure.Should().Be(SinkFailure.Rejected);
    }

    [Fact]
    public async Task should_not_read_an_answer_that_never_stops()
    {
        var huge = new string('x', 17 * 1024 * 1024);
        var http = new FakeHttp(_ => FakeHttp.Json($"[{{\"session\":\"{huge}\",\"lines\":1}}]"));

        var read = () => new MnemarrClient(http).PositionsAsync(Url, "k", "gemini", TestContext.Current.CancellationToken);

        (await read.Should().ThrowAsync<SinkException>()).Which.Failure.Should().Be(SinkFailure.Unreachable);
    }

    [Fact]
    public async Task should_not_swallow_the_caller_giving_up()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var http = new FakeHttp(_ => throw new TaskCanceledException());

        var send = () => new MnemarrClient(http).HeartbeatAsync(Url, "k", new Heartbeat("v", 0, null, null), cancelled.Token);

        await send.Should().ThrowAsync<OperationCanceledException>();
    }

    /// <summary>An answer that has sent its headers and then nothing.</summary>
    private sealed class HangingStream : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override void Flush() => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>An answer whose connection breaks while it is read.</summary>
    private sealed class BrokenStream : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => throw new IOException("The connection was closed.");

        public override int Read(byte[] buffer, int offset, int count) => throw new IOException("The connection was closed.");

        public override void Flush() => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
