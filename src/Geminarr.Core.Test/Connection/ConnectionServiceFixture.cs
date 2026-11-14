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
    private readonly LiveViewService _views;
    private readonly ConnectionTracker _tracker;

    public ConnectionServiceFixture()
    {
        _views = new LiveViewService(Options.Of(new BrowserOptions()), _clock);
        _tracker = Tracking.Of(new ConnectionStore(_workspace.Database, _clock), Options.Of(new ReaderOptions { LoginFailures = 2 }), _clock, _browser, _views);
    }

    public void Dispose() => _workspace.Dispose();

    private ConnectionService Subject(ReaderKind kind = ReaderKind.Web) => new(_tracker, _views, _browser, Options.Of(new ReaderOptions { Kind = kind }), _clock);

    [Theory]
    [InlineData(ReaderKind.None)]
    [InlineData(ReaderKind.Demo)]
    public void should_have_nothing_to_sign_in_to_when_no_browser_reads(ReaderKind kind)
    {
        var subject = Subject(kind);

        subject.HasBrowser.Should().BeFalse();
        var start = () => subject.StartSignIn();
        start.Should().Throw<InvalidOperationException>().Which.Message.Should().Contain("READER__KIND=Web");
        var pause = () => subject.Pause(null);
        pause.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void should_move_to_connecting_and_give_a_ticket_when_the_operator_asks_to_sign_in()
    {
        var subject = Subject();

        var ticket = subject.StartSignIn();

        ticket.Token.Should().NotBeNullOrEmpty();
        subject.Current().State.Should().Be(ConnectionState.Connecting);
    }

    [Fact]
    public void should_refuse_a_second_live_view_and_a_sign_in_while_paused()
    {
        var subject = Subject();
        subject.StartSignIn();
        var second = () => subject.StartSignIn();
        second.Should().Throw<LiveViewBusyException>();

        _views.Cancel();
        subject.Pause(null);
        var paused = () => subject.StartSignIn();
        paused.Should().Throw<InvalidOperationException>().Which.Message.Should().Contain("paused");
    }

    [Fact]
    public void should_be_connected_and_remember_the_session_when_the_person_signed_in()
    {
        var subject = Subject();
        subject.StartSignIn();

        subject.SignInEnded(signedIn: true);

        subject.Current().State.Should().Be(ConnectionState.Connected);
        _browser.Profile().HasSession.Should().BeTrue();
    }

    [Fact]
    public void should_stay_connected_when_a_person_who_was_already_signed_in_closes_the_live_view_without_signing_in_again()
    {
        var subject = Subject();
        subject.StartSignIn();
        subject.SignInEnded(signedIn: true);
        _views.Cancel();

        subject.StartSignIn();
        subject.CloseLiveView();

        subject.Current().State.Should().Be(ConnectionState.Connected, "trying to sign in again does not say that the session expired");
        subject.Current().LastReason.Should().BeNull();
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
    public void should_not_let_a_pass_overwrite_a_person_who_is_signing_in()
    {
        var subject = Subject();
        subject.StartSignIn();

        _tracker.Connected();
        _tracker.Failed(ConnectionReasons.Unreachable);
        _tracker.SignedOut();
        _tracker.NotConnected();

        subject.Current().State.Should().Be(ConnectionState.Connecting);
    }

    [Fact]
    public void should_go_back_to_not_connected_when_the_live_view_ended_without_a_session()
    {
        var subject = Subject();
        subject.StartSignIn();

        subject.SignInEnded(signedIn: false);

        subject.Current().State.Should().Be(ConnectionState.NotConnected);
    }

    [Fact]
    public void should_go_back_to_needs_login_when_the_profile_held_a_session_that_the_live_view_did_not_renew()
    {
        _browser.State = new ProfileState(Exists: true, HasSession: true, Private: true);
        var subject = Subject();
        subject.StartSignIn();

        subject.SignInEnded(signedIn: false);

        subject.Current().State.Should().Be(ConnectionState.NeedsLogin);
    }

    [Fact]
    public void should_not_touch_the_state_when_a_live_view_ends_after_the_state_was_settled()
    {
        var subject = Subject();
        subject.StartSignIn();
        subject.SignInEnded(signedIn: true);

        subject.SignInEnded(signedIn: false);

        subject.Current().State.Should().Be(ConnectionState.Connected);
    }

    [Fact]
    public void should_close_the_live_view_when_the_operator_asks_and_settle_the_state()
    {
        var subject = Subject();
        subject.StartSignIn();

        subject.CloseLiveView();

        _views.Active.Should().BeFalse();
        subject.Current().State.Should().Be(ConnectionState.NotConnected);
        subject.CloseLiveView();
    }

    [Fact]
    public void should_forget_a_live_view_that_nobody_opened_when_the_state_is_read()
    {
        var subject = Subject();
        subject.StartSignIn();

        _clock.Advance(LiveViewService.TokenLife + TimeSpan.FromSeconds(1));

        subject.Current().State.Should().Be(ConnectionState.NotConnected);
    }

    [Fact]
    public async Task should_destroy_the_profile_and_forget_the_connection_when_the_operator_disconnects()
    {
        var subject = Subject();
        subject.StartSignIn();
        subject.SignInEnded(signedIn: true);

        await subject.DisconnectAsync(TestContext.Current.CancellationToken);

        _browser.Destroyed.Should().Be(1);
        _views.Active.Should().BeFalse();
        subject.Current().State.Should().Be(ConnectionState.NotConnected);
        _browser.Profile().HasSession.Should().BeFalse();
    }

    [Fact]
    public void should_pause_until_a_moment_and_resume_by_itself_then_by_the_operator()
    {
        var subject = Subject();
        subject.StartSignIn();
        subject.SignInEnded(signedIn: true);

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
    public void should_end_a_live_view_that_is_open_when_the_operator_pauses()
    {
        var subject = Subject();
        subject.StartSignIn();

        subject.Pause(null);

        _views.Active.Should().BeFalse();
        subject.Current().State.Should().Be(ConnectionState.Paused);
    }
}
