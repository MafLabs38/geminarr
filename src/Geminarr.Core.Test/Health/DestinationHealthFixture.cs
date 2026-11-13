using System.Net;
using Geminarr.Core.Connection;
using Geminarr.Core.Health;
using Geminarr.Core.Link;
using Geminarr.Core.Pipeline;
using Geminarr.Core.Storage;
using Geminarr.Core.Test.Access;
using Geminarr.Core.Test.Support;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Geminarr.Core.Test.Health;

/// <summary>The checks of the database, the connection, the destination and the outbox: each says what is wrong with a stable code.</summary>
public sealed class DestinationHealthFixture : IDisposable
{
    private static readonly HealthCheckContext Context = new();

    private readonly TestDatabase _workspace = new();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 13, 9, 0, 0, TimeSpan.Zero));
    private readonly FakeKeys _keys = new();

    public void Dispose() => _workspace.Dispose();

    private static string? Reason(HealthCheckResult result) => result.Data.TryGetValue(HealthReasons.Key, out var reason) ? reason as string : null;

    private LinkService Link(MnemarrOptions? options = null) =>
        new(Options.Of(options ?? new MnemarrOptions()), new DestinationStore(_workspace.Database), _keys, new MnemarrClient(new FakeHttp(_ => FakeHttp.Status(HttpStatusCode.NotFound))), _clock, NullLogger<LinkService>.Instance, new OutboxStore(_workspace.Database));

    [Fact]
    public async Task should_say_the_database_is_healthy_when_it_opens()
    {
        var result = await new DatabaseHealthCheck(_workspace.Database).CheckHealthAsync(Context, TestContext.Current.CancellationToken);

        result.Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public async Task should_say_the_database_is_unreadable_when_the_folder_is_not_there()
    {
        var database = new Database(Options.Of(new Geminarr.Core.Configuration.GeminarrOptions { DataFolder = Path.Combine(_workspace.Folder, "missing") }));

        var result = await new DatabaseHealthCheck(database).CheckHealthAsync(Context, TestContext.Current.CancellationToken);

        result.Status.Should().Be(HealthStatus.Unhealthy);
        Reason(result).Should().Be(HealthReasons.DatabaseUnreadable);
        result.Description.Should().NotContain(_workspace.Folder);
    }

    [Fact]
    public async Task should_say_the_database_is_of_a_version_it_does_not_know()
    {
        using (var connection = _workspace.Database.Open())
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"PRAGMA user_version = {Schema.Version + 1};";
            command.ExecuteNonQuery();
        }

        var result = await new DatabaseHealthCheck(new Database(Options.Of(_workspace.Settings))).CheckHealthAsync(Context, TestContext.Current.CancellationToken);

        result.Status.Should().Be(HealthStatus.Unhealthy);
        Reason(result).Should().Be(HealthReasons.DatabaseStepUnknown);
    }

    [Fact]
    public async Task should_say_not_linked_then_healthy_once_an_address_and_a_key_exist()
    {
        var link = Link();
        var check = new MnemarrHealthCheck(link);
        var notLinked = await check.CheckHealthAsync(Context, TestContext.Current.CancellationToken);
        notLinked.Status.Should().Be(HealthStatus.Degraded);
        Reason(notLinked).Should().Be(DestinationProblems.NotLinked);

        link.SetAddress("https://mnemarr.homelab.example");
        _keys.Key = "k";

        (await check.CheckHealthAsync(Context, TestContext.Current.CancellationToken)).Status.Should().Be(HealthStatus.Healthy);
    }

    [Theory]
    [InlineData(DestinationProblems.Refused)]
    [InlineData(DestinationProblems.Rejected)]
    [InlineData(DestinationProblems.Unreachable)]
    public async Task should_say_what_the_last_talk_with_mnemarr_left(string problem)
    {
        var link = Link();
        link.SetAddress("https://mnemarr.homelab.example");
        _keys.Key = "k";
        link.RecordTalk(problem);

        var result = await new MnemarrHealthCheck(link).CheckHealthAsync(Context, TestContext.Current.CancellationToken);

        result.Status.Should().Be(HealthStatus.Degraded);
        Reason(result).Should().Be(problem);
    }

    [Fact]
    public async Task should_say_the_address_is_not_https()
    {
        var link = Link();
        link.SetAddress("http://mnemarr.homelab.example");
        _keys.Key = "k";

        var result = await new MnemarrHealthCheck(link).CheckHealthAsync(Context, TestContext.Current.CancellationToken);

        Reason(result).Should().Be(DestinationProblems.InsecureAddress);
    }

    [Fact]
    public async Task should_not_fail_when_the_destination_cannot_be_read()
    {
        var broken = new LinkService(
            Options.Of(new MnemarrOptions()),
            new DestinationStore(new Database(Options.Of(new Geminarr.Core.Configuration.GeminarrOptions { DataFolder = Path.Combine(_workspace.Folder, "missing") }))),
            _keys,
            new MnemarrClient(new FakeHttp(_ => FakeHttp.Status(HttpStatusCode.NotFound))),
            _clock,
            NullLogger<LinkService>.Instance, new OutboxStore(_workspace.Database));

        var result = await new MnemarrHealthCheck(broken).CheckHealthAsync(Context, TestContext.Current.CancellationToken);

        result.Status.Should().Be(HealthStatus.Degraded);
    }

    [Theory]
    [InlineData(ConnectionState.Connected, HealthStatus.Healthy, null)]
    [InlineData(ConnectionState.NotConnected, HealthStatus.Degraded, ConnectionReasons.NotConnected)]
    [InlineData(ConnectionState.NeedsLogin, HealthStatus.Degraded, ConnectionReasons.NeedsLogin)]
    [InlineData(ConnectionState.Degraded, HealthStatus.Degraded, ConnectionReasons.ReaderBroken)]
    public async Task should_say_the_state_of_the_connection_by_its_reason_code(ConnectionState state, HealthStatus status, string? reason)
    {
        var store = new ConnectionStore(_workspace.Database, _clock);
        store.Save(new ConnectionRecord(state, _clock.GetUtcNow(), null, 0, reason));
        var tracker = new ConnectionTracker(store, Options.Of(new ReaderOptions()), _clock);

        var result = await new ConnectionHealthCheck(tracker).CheckHealthAsync(Context, TestContext.Current.CancellationToken);

        result.Status.Should().Be(status);
        Reason(result).Should().Be(reason);
    }

    [Fact]
    public async Task should_say_a_degraded_connection_without_a_reason_is_unreachable()
    {
        var store = new ConnectionStore(_workspace.Database, _clock);
        store.Save(new ConnectionRecord(ConnectionState.Degraded, _clock.GetUtcNow(), null, 1, null));

        var result = await new ConnectionHealthCheck(new ConnectionTracker(store, Options.Of(new ReaderOptions()), _clock)).CheckHealthAsync(Context, TestContext.Current.CancellationToken);

        Reason(result).Should().Be(ConnectionReasons.Unreachable);
    }

    [Fact]
    public async Task should_say_the_outbox_grows_only_when_something_has_waited_for_more_than_an_hour()
    {
        var outbox = new OutboxStore(_workspace.Database);
        var check = new OutboxHealthCheck(outbox, _clock);
        (await check.CheckHealthAsync(Context, TestContext.Current.CancellationToken)).Status.Should().Be(HealthStatus.Healthy);

        outbox.Replace("c1", [(0, ["a"])], _clock.GetUtcNow());
        _clock.Advance(TimeSpan.FromMinutes(59));
        (await check.CheckHealthAsync(Context, TestContext.Current.CancellationToken)).Status.Should().Be(HealthStatus.Healthy);

        _clock.Advance(TimeSpan.FromMinutes(2));
        var growing = await check.CheckHealthAsync(Context, TestContext.Current.CancellationToken);

        growing.Status.Should().Be(HealthStatus.Degraded);
        Reason(growing).Should().Be(HealthReasons.OutboxGrowing);
    }

    [Fact]
    public async Task should_not_fail_when_the_outbox_or_the_connection_cannot_be_read()
    {
        var broken = new Database(Options.Of(new Geminarr.Core.Configuration.GeminarrOptions { DataFolder = Path.Combine(_workspace.Folder, "missing") }));

        var outbox = await new OutboxHealthCheck(new OutboxStore(broken), _clock).CheckHealthAsync(Context, TestContext.Current.CancellationToken);
        var connection = await new ConnectionHealthCheck(new ConnectionTracker(new ConnectionStore(broken, _clock), Options.Of(new ReaderOptions()), _clock)).CheckHealthAsync(Context, TestContext.Current.CancellationToken);

        outbox.Status.Should().Be(HealthStatus.Degraded);
        connection.Status.Should().Be(HealthStatus.Degraded);
    }
}
