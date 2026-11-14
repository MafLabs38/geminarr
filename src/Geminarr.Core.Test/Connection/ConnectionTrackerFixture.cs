using Geminarr.Core.Connection;
using Geminarr.Core.Pipeline;
using Geminarr.Core.Test.Access;
using Geminarr.Core.Test.Support;
using Microsoft.Extensions.Time.Testing;

namespace Geminarr.Core.Test.Connection;

public sealed class ConnectionTrackerFixture : IDisposable
{
    private readonly TestDatabase _workspace = new();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 13, 9, 0, 0, TimeSpan.Zero));

    public void Dispose() => _workspace.Dispose();

    private ConnectionTracker Subject(int loginFailures = 3) =>
        Tracking.Of(new ConnectionStore(_workspace.Database, _clock), Options.Of(new ReaderOptions { LoginFailures = loginFailures }), _clock);

    [Fact]
    public void should_be_not_connected_until_a_pass_has_read()
    {
        Subject().Current().State.Should().Be(ConnectionState.NotConnected);
    }

    [Fact]
    public void should_not_count_failures_of_another_kind_towards_needing_a_login()
    {
        var subject = Subject(loginFailures: 2);
        subject.Connected();
        subject.Failed(ConnectionReasons.Unreachable);
        subject.Failed(ConnectionReasons.Unreachable);

        subject.SignedOut().State.Should().NotBe(ConnectionState.NeedsLogin, "two network failures and one signed-out are not two times signed out");
        subject.SignedOut().State.Should().Be(ConnectionState.NeedsLogin, "the second signed-out in a row is");
    }

    [Fact]
    public void should_be_connected_after_a_good_pass_and_remember_when()
    {
        var subject = Subject();

        var record = subject.Connected();

        record.State.Should().Be(ConnectionState.Connected);
        record.LastOkAt.Should().Be(_clock.GetUtcNow());
        record.ConsecutiveFailures.Should().Be(0);
        record.LastReason.Should().BeNull();
    }

    [Fact]
    public void should_keep_since_while_the_state_does_not_change_and_move_it_when_it_does()
    {
        var subject = Subject();
        var first = subject.Connected();
        _clock.Advance(TimeSpan.FromMinutes(20));

        var again = subject.Connected();
        var failed = subject.Failed(ConnectionReasons.Unreachable);

        again.Since.Should().Be(first.Since);
        failed.Since.Should().Be(_clock.GetUtcNow());
        failed.State.Should().Be(ConnectionState.Degraded);
        failed.LastReason.Should().Be(ConnectionReasons.Unreachable);
        failed.LastOkAt.Should().Be(again.LastOkAt, "the last pass that worked is still the one before");
    }

    [Fact]
    public void should_turn_to_needs_login_only_after_several_passes_in_a_row_found_nobody_signed_in()
    {
        var subject = Subject();
        subject.Connected();

        subject.SignedOut().State.Should().Be(ConnectionState.Connected);
        subject.SignedOut().State.Should().Be(ConnectionState.Connected);
        var third = subject.SignedOut();

        third.State.Should().Be(ConnectionState.NeedsLogin);
        third.LastReason.Should().Be(ConnectionReasons.NeedsLogin);
        third.ConsecutiveFailures.Should().Be(3);
    }

    [Fact]
    public void should_be_in_doubt_and_not_connected_when_a_connection_never_made_finds_nobody_signed_in()
    {
        var subject = Subject();

        var record = subject.SignedOut();

        record.State.Should().Be(ConnectionState.Degraded);
        record.LastReason.Should().Be(ConnectionReasons.NeedsLogin);
    }

    [Fact]
    public void should_forget_the_failures_when_somebody_signs_in_again()
    {
        var subject = Subject(loginFailures: 1);
        subject.SignedOut().State.Should().Be(ConnectionState.NeedsLogin);

        var record = subject.Connected();

        record.State.Should().Be(ConnectionState.Connected);
        record.ConsecutiveFailures.Should().Be(0);
    }

    [Fact]
    public void should_count_the_passes_that_failed_in_a_row()
    {
        var subject = Subject();

        subject.Failed(ConnectionReasons.ReaderBroken);
        var second = subject.Failed(ConnectionReasons.ReaderBroken);

        second.ConsecutiveFailures.Should().Be(2);
        subject.NotConnected().State.Should().Be(ConnectionState.NotConnected);
        subject.Current().ConsecutiveFailures.Should().Be(0);
    }

    [Fact]
    public void should_keep_the_moment_a_pause_ends_and_forget_it_when_it_is_over()
    {
        var store = new ConnectionStore(_workspace.Database, _clock);
        var subject = Tracking.Of(store, Options.Of(new ReaderOptions()), _clock);
        var until = _clock.GetUtcNow().AddHours(1);

        subject.Paused(until);

        store.Get().Should().Match<ConnectionRecord>(record => record.State == ConnectionState.Paused && record.PausedUntil == until && record.LastReason == ConnectionReasons.Paused);
        subject.Connected();
        store.Get().PausedUntil.Should().BeNull();
    }
}
