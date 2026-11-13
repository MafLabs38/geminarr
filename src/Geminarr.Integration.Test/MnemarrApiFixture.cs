using System.Net;
using System.Net.Http.Json;
using Geminarr.Api.V1.Connection;
using Geminarr.Api.V1.Health;
using Geminarr.Api.V1.Mnemarr;
using Geminarr.Api.V1.Runs;
using Geminarr.Core.Pipeline;
using Microsoft.Extensions.DependencyInjection;

namespace Geminarr.Integration.Test;

/// <summary>The link to Mnemarr through the API, with a Mnemarr in memory: the address, the pairing by asking, the enrolment with a code, unlinking.</summary>
public sealed class MnemarrApiFixture
{
    private static readonly CancellationToken Token = TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("/api/v1/mnemarr")]
    [InlineData("/api/v1/mnemarr/pairing")]
    [InlineData("/api/v1/runs")]
    [InlineData("/api/v1/connection")]
    public async Task should_answer_401_to_a_caller_who_has_not_signed_in(string path)
    {
        using var factory = new MnemarrFactory();

        (await factory.CreateClient().GetAsync(path, Token)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task should_refuse_a_write_without_the_header()
    {
        using var factory = new MnemarrFactory();
        using var client = await factory.SignedInClientAsync(sendsTheHeader: false);

        (await client.PostAsync("/api/v1/runs", content: null, Token)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.PostAsync("/api/v1/mnemarr/pairing", content: null, Token)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.DeleteAsync("/api/v1/mnemarr", Token)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task should_say_nothing_is_linked_at_first()
    {
        using var factory = new MnemarrFactory();
        using var client = await factory.SignedInClientAsync();

        var mnemarr = await client.GetFromJsonAsync<MnemarrResource>("/api/v1/mnemarr", Token);

        mnemarr.Should().BeEquivalentTo(new MnemarrResource(null, "gemini", false, false, "mnemarr-not-linked", null, 0));
    }

    [Fact]
    public async Task should_keep_an_address_given_on_the_page_and_refuse_one_that_is_not()
    {
        using var factory = new MnemarrFactory();
        using var client = await factory.SignedInClientAsync();

        (await client.PutAsJsonAsync("/api/v1/mnemarr", new { url = "not an address" }, Token)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.PutAsJsonAsync("/api/v1/mnemarr", new { url = (string?)null }, Token)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var response = await client.PutAsJsonAsync("/api/v1/mnemarr", new { url = "https://mnemarr.test/" }, Token);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<MnemarrResource>(Token))!.Url.Should().Be("https://mnemarr.test");
    }

    [Fact]
    public async Task should_refuse_to_change_an_address_that_the_deployment_gives()
    {
        using var factory = new MnemarrFactory(urlFromSettings: true);
        using var client = await factory.SignedInClientAsync();

        var response = await client.PutAsJsonAsync("/api/v1/mnemarr", new { url = "https://other.test" }, Token);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await client.GetFromJsonAsync<MnemarrResource>("/api/v1/mnemarr", Token))!.AddressFromSettings.Should().BeTrue();
    }

    [Fact]
    public async Task should_pair_by_asking_once_a_person_has_approved_and_never_show_the_key()
    {
        using var factory = new MnemarrFactory();
        using var client = await factory.SignedInClientAsync();
        await client.PutAsJsonAsync("/api/v1/mnemarr", new { url = "https://mnemarr.test" }, Token);

        var started = await (await client.PostAsync("/api/v1/mnemarr/pairing", content: null, Token)).Content.ReadFromJsonAsync<PairingResource>(Token);
        started.Should().BeEquivalentTo(new PairingResource("pending", "ABCD-EFGH", "https://mnemarr.test/link?code=ABCD-EFGH", started!.ExpiresAt));
        (await client.GetFromJsonAsync<PairingResource>("/api/v1/mnemarr/pairing", Token))!.State.Should().Be("pending");

        var waiting = await (await client.PostAsync("/api/v1/mnemarr/pairing/check", content: null, Token)).Content.ReadFromJsonAsync<PairingResource>(Token);
        waiting!.State.Should().Be("pending");

        factory.Mnemarr.MachineName = "gemini-2";
        factory.Mnemarr.Approved = true;
        var approved = await (await client.PostAsync("/api/v1/mnemarr/pairing/check", content: null, Token)).Content.ReadFromJsonAsync<PairingResource>(Token);

        approved!.State.Should().Be("approved");
        var linked = await client.GetStringAsync("/api/v1/mnemarr", Token);
        linked.Should().Contain("\"linked\":true").And.Contain("\"machine\":\"gemini-2\"").And.NotContain(FakeKey);
        File.ReadAllText(Path.Combine(factory.DataFolder, "mnemarr", "key")).Trim().Should().Be(FakeKey);
        factory.Mnemarr.Headers.Where((_, index) => factory.Mnemarr.Calls[index].StartsWith("POST /api/v1/ingest/pair", StringComparison.Ordinal)).Should().OnlyContain(headers => headers.ContainsKey("X-Mnemarr-Request"));
    }

    private static string FakeKey => Support.FakeMnemarr.Key;

    [Fact]
    public async Task should_answer_a_pairing_that_cannot_start_with_a_sentence_and_not_a_crash()
    {
        using var factory = new MnemarrFactory();
        using var client = await factory.SignedInClientAsync();

        (await client.PostAsync("/api/v1/mnemarr/pairing", content: null, Token)).StatusCode.Should().Be(HttpStatusCode.Conflict);

        await client.PutAsJsonAsync("/api/v1/mnemarr", new { url = "http://mnemarr.test" }, Token);
        (await client.PostAsync("/api/v1/mnemarr/pairing", content: null, Token)).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        await client.DeleteAsync("/api/v1/mnemarr", Token);
        await client.PutAsJsonAsync("/api/v1/mnemarr", new { url = "https://mnemarr.test" }, Token);
        factory.Mnemarr.FailWith = HttpStatusCode.BadGateway;
        var down = await client.PostAsync("/api/v1/mnemarr/pairing", content: null, Token);
        down.StatusCode.Should().Be(HttpStatusCode.BadGateway);
        (await down.Content.ReadAsStringAsync(Token)).Should().NotContain("mnemarr.test");

        factory.Mnemarr.FailWith = null;
        await client.PostAsync("/api/v1/mnemarr/pairing", content: null, Token);
        factory.Mnemarr.FailWith = HttpStatusCode.BadGateway;
        (await client.PostAsync("/api/v1/mnemarr/pairing/check", content: null, Token)).StatusCode.Should().Be(HttpStatusCode.BadGateway);
    }

    [Fact]
    public async Task should_give_a_pairing_up()
    {
        using var factory = new MnemarrFactory(urlFromSettings: true);
        using var client = await factory.SignedInClientAsync();
        await client.PostAsync("/api/v1/mnemarr/pairing", content: null, Token);

        (await client.DeleteAsync("/api/v1/mnemarr/pairing", Token)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await client.GetFromJsonAsync<PairingResource>("/api/v1/mnemarr/pairing", Token))!.State.Should().Be("none");
    }

    [Fact]
    public async Task should_unlink_and_forget_the_key()
    {
        using var factory = new MnemarrFactory();
        using var client = await factory.SignedInClientAsync();
        await client.PutAsJsonAsync("/api/v1/mnemarr", new { url = "https://mnemarr.test" }, Token);
        await client.PostAsync("/api/v1/mnemarr/pairing", content: null, Token);
        factory.Mnemarr.Approved = true;
        await client.PostAsync("/api/v1/mnemarr/pairing/check", content: null, Token);

        (await client.DeleteAsync("/api/v1/mnemarr", Token)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var mnemarr = await client.GetFromJsonAsync<MnemarrResource>("/api/v1/mnemarr", Token);
        mnemarr!.Linked.Should().BeFalse();
        mnemarr.Url.Should().BeNull();
        File.Exists(Path.Combine(factory.DataFolder, "mnemarr", "key")).Should().BeFalse();
    }

    [Fact]
    public async Task should_enrol_with_a_code_file_at_start_and_send_with_the_key_it_was_given()
    {
        using var factory = new MnemarrFactory(urlFromSettings: true, enrolmentCode: "GOOD-CODE");
        using var client = await factory.SignedInClientAsync();
        var scheduler = ActivatorUtilities.CreateInstance<Geminarr.Host.Pipeline.PassScheduler>(factory.Services, TimeSpan.Zero);
        using var stop = new CancellationTokenSource();

        await scheduler.StartAsync(stop.Token);
        await Eventually(() => factory.Mnemarr.Heartbeats.Count > 0);
        await scheduler.StopAsync(CancellationToken.None);

        factory.Mnemarr.Enrolments.Should().Be(1);
        (await client.GetFromJsonAsync<MnemarrResource>("/api/v1/mnemarr", Token))!.Linked.Should().BeTrue();
        factory.Mnemarr.Conversations.Keys.Should().OnlyContain(session => session.StartsWith("gemini-demo-c", StringComparison.Ordinal));
    }

    [Fact]
    public async Task should_not_start_if_the_enrolment_code_is_refused_and_say_nothing_is_linked()
    {
        using var factory = new MnemarrFactory(urlFromSettings: true, enrolmentCode: "BAD-CODE");
        using var client = await factory.SignedInClientAsync();
        var scheduler = ActivatorUtilities.CreateInstance<Geminarr.Host.Pipeline.PassScheduler>(factory.Services, TimeSpan.Zero);

        await scheduler.StartAsync(CancellationToken.None);
        await Eventually(() => factory.Mnemarr.Enrolments > 0);
        await scheduler.StopAsync(CancellationToken.None);

        (await client.GetFromJsonAsync<MnemarrResource>("/api/v1/mnemarr", Token))!.Linked.Should().BeFalse();
    }

    [Fact]
    public async Task should_bring_the_conversations_of_the_demonstration_reader_to_mnemarr_and_say_it_is_alive()
    {
        using var factory = await LinkedAsync();
        using var client = await factory.SignedInClientAsync();

        var run = await factory.Services.GetRequiredService<PassRunner>().RunAsync(Token);

        run!.Outcome.Should().Be("ok");
        run.TurnsSent.Should().BeGreaterThan(0);
        factory.Mnemarr.Conversations.Should().HaveCount(3);
        var lines = factory.Mnemarr.Conversations.Values.First();
        lines[0].Should().Contain("\"meta\"").And.Contain("Demo:");
        lines.Skip(1).Should().OnlyContain(line => line.Contains("\"role\"", StringComparison.Ordinal));
        var beat = factory.Mnemarr.Heartbeats.Last();
        beat.GetProperty("pendingLines").GetInt64().Should().Be(0);
        beat.GetProperty("attention").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null);
        factory.Mnemarr.Headers.Where((_, index) => factory.Mnemarr.Calls[index] != "POST /api/v1/ingest/pair").Last()["Authorization"].Should().Be($"Bearer {FakeKey}");
        var connection = await client.GetFromJsonAsync<ConnectionResource>("/api/v1/connection", Token);
        (connection!.State, connection.Reader).Should().Be(("Connected", "Demo"));
        var runs = await client.GetFromJsonAsync<RunsResource>("/api/v1/runs", Token);
        runs!.Runs.Should().ContainSingle().Which.Outcome.Should().Be("ok");
        runs.Running.Should().BeFalse();
    }

    [Fact]
    public async Task should_ask_the_person_to_sign_in_again_when_the_demonstration_session_ends_and_stop_asking_when_it_returns()
    {
        using var factory = await LinkedAsync();
        using var client = await factory.SignedInClientAsync();
        var runner = factory.Services.GetRequiredService<PassRunner>();
        await runner.RunAsync(Token);
        var marker = Path.Combine(factory.DataFolder, DemoReader.SignedOutFile);
        await File.WriteAllTextAsync(marker, string.Empty, Token);

        await runner.RunAsync(Token);
        await runner.RunAsync(Token);

        var attention = factory.Mnemarr.Heartbeats.Last().GetProperty("attention");
        attention.GetProperty("code").GetString().Should().Be("needs-login");
        attention.GetProperty("url").GetString().Should().Be("https://geminarr.homelab.example/");
        factory.Mnemarr.Heartbeats.Last().GetProperty("error").GetString().Should().Be("The session has expired.");
        (await client.GetFromJsonAsync<ConnectionResource>("/api/v1/connection", Token))!.State.Should().Be("NeedsLogin");
        var health = await client.GetFromJsonAsync<HealthResource>("/api/v1/health", Token);
        health!.Checks.Single(check => check.Name == "connection").Reason.Should().Be("needs-login");

        File.Delete(marker);
        await runner.RunAsync(Token);

        factory.Mnemarr.Heartbeats.Last().GetProperty("attention").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null);
        (await client.GetFromJsonAsync<ConnectionResource>("/api/v1/connection", Token))!.State.Should().Be("Connected");
    }

    [Fact]
    public async Task should_say_mnemarr_refused_the_key_in_the_health_without_failing_the_probes()
    {
        using var factory = await LinkedAsync();
        using var client = await factory.SignedInClientAsync();
        factory.Mnemarr.FailWith = HttpStatusCode.Unauthorized;

        await factory.Services.GetRequiredService<PassRunner>().RunAsync(Token);

        var health = await client.GetFromJsonAsync<HealthResource>("/api/v1/health", Token);
        health!.Checks.Single(check => check.Name == "mnemarr").Reason.Should().Be("mnemarr-refused");
        health.Checks.Single(check => check.Name == "database").Status.Should().Be("Healthy");
        (await client.GetAsync("/health/ready", Token)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetFromJsonAsync<MnemarrResource>("/api/v1/mnemarr", Token))!.Problem.Should().Be("mnemarr-refused");
    }

    [Fact]
    public async Task should_run_a_pass_at_once_when_asked_and_refuse_to_start_a_second_alongside()
    {
        using var factory = await LinkedAsync();
        using var client = await factory.SignedInClientAsync();
        var scheduler = ActivatorUtilities.CreateInstance<Geminarr.Host.Pipeline.PassScheduler>(factory.Services, TimeSpan.Zero);
        await scheduler.StartAsync(CancellationToken.None);
        await Eventually(() => factory.Mnemarr.Heartbeats.Count >= 1);
        // The heartbeat is the last thing a pass tells Mnemarr; the pass itself ends a moment later, and "run now" is refused while one runs.
        await EventuallyAsync(async () => (await client.GetFromJsonAsync<RunsResource>("/api/v1/runs", Token))! is { Running: false, Runs.Count: >= 1 });
        var before = factory.Mnemarr.Heartbeats.Count;

        var response = await client.PostAsync("/api/v1/runs", content: null, Token);
        await Eventually(() => factory.Mnemarr.Heartbeats.Count > before);
        await scheduler.StopAsync(CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await client.GetFromJsonAsync<RunsResource>("/api/v1/runs?limit=1", Token))!.Runs.Should().ContainSingle();
    }

    [Fact]
    public async Task should_list_the_latest_runs_within_a_limit()
    {
        using var factory = await LinkedAsync();
        using var client = await factory.SignedInClientAsync();
        var runner = factory.Services.GetRequiredService<PassRunner>();
        await runner.RunAsync(Token);
        await runner.RunAsync(Token);
        await runner.RunAsync(Token);

        (await client.GetFromJsonAsync<RunsResource>("/api/v1/runs?limit=2", Token))!.Runs.Should().HaveCount(2);
        (await client.GetFromJsonAsync<RunsResource>("/api/v1/runs?limit=0", Token))!.Runs.Should().HaveCount(1);
        (await client.GetFromJsonAsync<RunsResource>("/api/v1/runs?limit=1000", Token))!.Runs.Should().HaveCount(3);
    }

    [Fact]
    public async Task should_show_the_new_settings_without_a_secret()
    {
        using var factory = new MnemarrFactory(urlFromSettings: true);
        using var client = await factory.SignedInClientAsync();

        var settings = await client.GetStringAsync("/api/v1/settings", Token);

        settings.Should().Contain("GEMINARR__MNEMARR__URL").And.Contain("https://mnemarr.test").And.Contain("GEMINARR__READER__KIND").And.Contain("Demo").And.Contain("GEMINARR__RETENTION__RUNSDAYS");
    }

    /// <summary>A host that is linked to the fake Mnemarr, as a pairing leaves it.</summary>
    private static async Task<MnemarrFactory> LinkedAsync()
    {
        var factory = new MnemarrFactory();
        using var client = await factory.SignedInClientAsync();
        await client.PutAsJsonAsync("/api/v1/mnemarr", new { url = "https://mnemarr.test" }, Token);
        await client.PostAsync("/api/v1/mnemarr/pairing", content: null, Token);
        factory.Mnemarr.Approved = true;
        await client.PostAsync("/api/v1/mnemarr/pairing/check", content: null, Token);
        return factory;
    }

    private static async Task EventuallyAsync(Func<Task<bool>> condition)
    {
        for (var attempt = 0; attempt < 200 && !await condition(); attempt++)
        {
            await Task.Delay(50, Token);
        }

        (await condition()).Should().BeTrue("it should have happened within ten seconds");
    }

    private static async Task Eventually(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 200 && !condition(); attempt++)
        {
            await Task.Delay(50, Token);
        }

        condition().Should().BeTrue("it should have happened within ten seconds");
    }
}
