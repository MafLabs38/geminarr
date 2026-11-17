using Geminarr.Core.Browser;
using Geminarr.Core.Connection;
using Geminarr.Core.Pipeline;
using Geminarr.Core.Test.Access;
using Geminarr.Core.Test.Support;
using Microsoft.Extensions.Time.Testing;

namespace Geminarr.Core.Test.Connection;

public sealed class ConnectionServiceFixture : IDisposable
{
    private readonly TestDatabase _workspace = new();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 13, 9, 0, 0, TimeSpan.Zero));
    private readonly FakeBrowser _browser = new();
    private readonly ConnectionTracker _tracker;
    private readonly SignInObserver _observer;

    public ConnectionServiceFixture()
    {
        _observer = new SignInObserver(_clock);
        _tracker = Tracking.Of(new ConnectionStore(_workspace.Database, _clock), Options.Of(new ReaderOptions { LoginFailures = 2 }), _clock);
    }

    public void Dispose() => _workspace.Dispose();

    private ConnectionService Subject(ReaderKind kind = ReaderKind.Web) => new(_tracker, _browser, Options.Of(new ReaderOptions { Kind = kind }), _clock, _observer);

    [Fact]
    public void should_keep_saying_signing_in_for_a_moment_after_the_sign_in_page_is_gone_unless_the_session_is_already_good()
    {
        var subject = Subject();
        subject.SigningIn.Should().BeFalse();

        _observer.Set(true);
        subject.SigningIn.Should().BeTrue("a tab is on the sign-in page");

        _observer.Set(false);
        subject.SigningIn.Should().BeTrue("the session is being looked at");

        _clock.Advance(TimeSpan.FromSeconds(14));
        subject.SigningIn.Should().BeTrue("still within the moment");

        _clock.Advance(TimeSpan.FromSeconds(2));
        subject.SigningIn.Should().BeFalse("the sign-in was abandoned: nothing is waited for any longer");

        _observer.Set(true);
        _observer.Set(false);
        _tracker.Connected();
        subject.SigningIn.Should().BeFalse("the check found the session good");
    }

    [Fact]
    public void should_show_nobody_as_signed_in_while_the_connection_is_not_one()
    {
        _browser.Account = new AccountIdentity("Jane Doe", "jane@example.org");
        var subject = Subject();
        _tracker.Connected();
        subject.Account.Should().NotBeNull();

        _tracker.Disconnected();

        subject.Account.Should().BeNull("a disconnect that did not reach the browser leaves the account in it, and the page must not say signed in beside not connected");
    }

    [Fact]
    public async Task should_open_the_sign_in_page_of_the_browser_and_refuse_when_no_browser_reads()
    {
        await Subject().OpenSignInAsync(TestContext.Current.CancellationToken);

        _browser.SignInPagesOpened.Should().Be(1);
        var act = () => Subject(ReaderKind.Demo).OpenSignInAsync(TestContext.Current.CancellationToken);
        await act.Should().ThrowAsync<ConnectionRefusedException>();
    }

    [Theory]
    [InlineData(ReaderKind.None)]
    [InlineData(ReaderKind.Demo)]
    public void should_have_nothing_to_pause_when_no_browser_reads(ReaderKind kind)
    {
        var subject = Subject(kind);

        subject.HasBrowser.Should().BeFalse();
        var pause = () => subject.Pause(null);
        pause.Should().Throw<ConnectionRefusedException>().Which.Message.Should().Contain("No browser reads");
    }

    [Fact]
    public void should_pause_until_a_moment_and_resume_by_itself_then_by_the_operator()
    {
        var subject = Subject();
        _tracker.Connected();

        subject.Pause(TimeSpan.FromHours(2));
        subject.Current().State.Should().Be(ConnectionState.Paused);
        subject.Current().PausedUntil.Should().Be(_clock.GetUtcNow().AddHours(2));
        _clock.Advance(TimeSpan.FromHours(2) + TimeSpan.FromSeconds(1));
        subject.Current().State.Should().Be(ConnectionState.Connected, "the pause is over");

        subject.Pause(null);
        subject.Current().PausedUntil.Should().BeNull();
        subject.Resume();
        subject.Current().State.Should().Be(ConnectionState.Connected);
        subject.Resume();
        subject.Current().State.Should().Be(ConnectionState.Connected, "resuming what is not paused changes nothing");
    }

    [Fact]
    public void should_resume_to_not_connected_when_no_pass_ever_worked()
    {
        var subject = Subject();
        subject.Pause(null);

        subject.Resume();

        subject.Current().State.Should().Be(ConnectionState.NotConnected);
    }

    [Fact]
    public void should_keep_what_was_known_through_a_pause_and_a_resume()
    {
        var subject = Subject();
        _tracker.SignedOut();
        _tracker.SignedOut();
        var before = subject.Current();
        before.State.Should().Be(ConnectionState.NeedsLogin);

        subject.Pause(null);
        subject.Resume();

        subject.Current().Should().Match<ConnectionRecord>(record => record.State == ConnectionState.NeedsLogin && record.ConsecutiveFailures == before.ConsecutiveFailures && record.LastReason == ConnectionReasons.NeedsLogin);
    }

    [Fact]
    public void should_go_back_to_a_degraded_state_with_its_reason_and_count_after_a_pause()
    {
        var subject = Subject();
        _tracker.Connected();
        _tracker.Failed(ConnectionReasons.RateLimited);
        _tracker.Failed(ConnectionReasons.RateLimited);

        subject.Pause(null);
        subject.Resume();

        subject.Current().Should().Match<ConnectionRecord>(record => record.State == ConnectionState.Degraded && record.LastReason == ConnectionReasons.RateLimited && record.ConsecutiveFailures == 2);
    }

    [Fact]
    public void should_not_let_a_pass_that_was_running_undo_a_pause()
    {
        var subject = Subject();
        _tracker.Connected();
        subject.Pause(TimeSpan.FromHours(1));

        _tracker.Connected();
        _tracker.Failed(ConnectionReasons.Unreachable);

        subject.Current().State.Should().Be(ConnectionState.Paused);
        subject.Current().PausedUntil.Should().NotBeNull();
    }

    [Fact]
    public async Task should_clear_the_session_of_the_browser_and_forget_the_connection_when_the_operator_disconnects()
    {
        var subject = Subject();
        _tracker.Connected();

        await subject.DisconnectAsync(TestContext.Current.CancellationToken);

        _browser.Disconnected.Should().Be(1);
        subject.Current().State.Should().Be(ConnectionState.NotConnected);
        subject.Current().LastOkAt.Should().BeNull();
    }

    [Fact]
    public async Task should_forget_the_connection_all_the_same_when_the_browser_cannot_be_reached_and_say_so()
    {
        var subject = Subject();
        _tracker.Connected();
        _browser.FailDisconnect = new ReaderException(ConnectionReasons.BrowserFailed, "The browser container could not be reached.");

        var disconnect = () => subject.DisconnectAsync(TestContext.Current.CancellationToken);

        await disconnect.Should().ThrowAsync<ReaderException>();
        subject.Current().State.Should().Be(ConnectionState.NotConnected);
    }

    [Fact]
    public async Task should_not_let_a_pass_that_began_before_a_disconnect_say_what_it_found_afterwards()
    {
        var subject = Subject();
        _tracker.Connected();
        using var pass = _tracker.BeginPass();

        await subject.DisconnectAsync(TestContext.Current.CancellationToken);
        _tracker.Connected();
        _tracker.NotConnected();

        subject.Current().State.Should().Be(ConnectionState.NotConnected);
        subject.Current().LastOkAt.Should().BeNull();
    }

    [Fact]
    public async Task should_stay_not_connected_through_a_pause_and_a_resume_after_a_disconnect()
    {
        var subject = Subject();
        _tracker.Connected();
        await subject.DisconnectAsync(TestContext.Current.CancellationToken);

        subject.Pause(null);
        subject.Resume();

        subject.Current().State.Should().Be(ConnectionState.NotConnected, "there is no session to resume to");
        subject.Current().LastOkAt.Should().BeNull();
    }
}
