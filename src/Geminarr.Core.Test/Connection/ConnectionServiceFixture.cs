using Geminarr.Core.Browser;
using Geminarr.Core.Connection;
using Geminarr.Core.Extension;
using Geminarr.Core.Pipeline;
using Geminarr.Core.Test.Access;
using Geminarr.Core.Test.Support;
using Microsoft.Extensions.Time.Testing;

namespace Geminarr.Core.Test.Connection;

public sealed class ConnectionServiceFixture : IDisposable
{
    private readonly TestDatabase _workspace = new();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 13, 9, 0, 0, TimeSpan.Zero));
    private readonly ExtensionInbox _inbox = new();
    private readonly ConnectionTracker _tracker;

    public ConnectionServiceFixture()
    {
        _tracker = Tracking.Of(new ConnectionStore(_workspace.Database, _clock), Options.Of(new ReaderOptions { LoginFailures = 2 }), _clock);
    }

    public void Dispose() => _workspace.Dispose();

    private ConnectionService Subject(ReaderKind kind = ReaderKind.Web) => new(_tracker, _inbox, Options.Of(new ReaderOptions { Kind = kind }), _clock);

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
    public void should_ask_the_extension_to_sign_the_account_out_and_forget_the_connection_when_the_operator_disconnects()
    {
        var subject = Subject();
        _tracker.Connected();

        subject.Disconnect();

        _inbox.Disconnected.Should().BeTrue();
        _inbox.TakeHints(_clock.GetUtcNow(), TimeSpan.FromMinutes(15)).SignOut.Should().BeTrue("the extension is asked the next time that it says that it is there");
        subject.Current().State.Should().Be(ConnectionState.NotConnected);
        subject.Current().LastReason.Should().Be(ConnectionReasons.Disconnected, "the record says so, so that a restart asks again");
        subject.Current().LastOkAt.Should().BeNull();
        _tracker.NotConnected().LastReason.Should().Be(ConnectionReasons.Disconnected, "a pass that finds nothing to read does not forget it");
        _tracker.Connected().LastReason.Should().BeNull();
    }

    [Fact]
    public void should_say_signing_in_from_the_press_of_the_button_until_the_session_is_good()
    {
        var subject = Subject();
        subject.SigningInAt(subject.Current()).Should().BeFalse();

        subject.BeginSigningIn();
        subject.SigningInAt(subject.Current()).Should().BeTrue("the operator pressed Sign in");

        _tracker.Connected();
        subject.SigningInAt(subject.Current()).Should().BeFalse("the session is good");
    }

    [Fact]
    public void should_show_who_the_browser_is_signed_in_as_only_while_the_connection_is_one()
    {
        var subject = Subject();
        _inbox.RecordSession(signedIn: true, "1.0.0", _clock.GetUtcNow(), ["Google Account: Jane Doe (jane@example.org)"]);
        _tracker.Connected();

        subject.AccountAt(subject.Current()).Should().Be(new AccountIdentity("Jane Doe", "jane@example.org"));

        _tracker.Disconnected();

        subject.AccountAt(subject.Current()).Should().BeNull("the page must not say signed in beside not connected");
    }

    [Fact]
    public void should_not_let_a_pass_that_began_before_a_disconnect_say_what_it_found_afterwards()
    {
        var subject = Subject();
        _tracker.Connected();
        using var pass = _tracker.BeginPass();

        subject.Disconnect();
        _tracker.Connected();
        _tracker.NotConnected();

        subject.Current().State.Should().Be(ConnectionState.NotConnected);
        subject.Current().LastOkAt.Should().BeNull();
    }

    [Fact]
    public void should_stay_not_connected_through_a_pause_and_a_resume_after_a_disconnect()
    {
        var subject = Subject();
        _tracker.Connected();
        subject.Disconnect();

        subject.Pause(null);
        subject.Resume();

        subject.Current().State.Should().Be(ConnectionState.NotConnected, "there is no session to resume to");
        subject.Current().LastOkAt.Should().BeNull();
    }

    [Fact]
    public void should_ask_for_the_sign_out_again_after_a_restart_when_the_record_says_that_the_operator_disconnected()
    {
        var record = new ConnectionRecord(ConnectionState.NotConnected, _clock.GetUtcNow(), null, 0, ConnectionReasons.Disconnected);

        var restored = ExtensionInbox.Restored(record);

        restored.Disconnected.Should().BeTrue();
        restored.SignOutPending.Should().BeTrue();
        ExtensionInbox.Restored(record with { LastReason = ConnectionReasons.NotConnected }).Disconnected.Should().BeFalse();
    }

    [Fact]
    public void should_keep_a_disconnect_that_waits_through_a_pause_and_ask_for_it_after_a_restart_in_the_middle_of_one_or_after_it()
    {
        var subject = Subject();
        subject.Disconnect();

        subject.Pause(null);
        ExtensionInbox.Restored(subject.Current()).Disconnected.Should().BeTrue("a restart while paused");

        subject.Resume();
        subject.Current().LastReason.Should().Be(ConnectionReasons.Disconnected);
        ExtensionInbox.Restored(subject.Current()).Disconnected.Should().BeTrue("a restart after the resume");
    }

    [Fact]
    public void should_forget_in_the_record_a_disconnect_that_the_operator_cancels_and_not_by_opening_the_desktop()
    {
        var subject = Subject();
        _tracker.Connected();
        subject.Disconnect();

        subject.BeginSigningIn();

        subject.Current().LastReason.Should().Be(ConnectionReasons.Disconnected, "opening the desktop does not give a pending sign-out up");
        _inbox.SignOutPending.Should().BeTrue();

        subject.CancelDisconnect();

        subject.Current().LastReason.Should().Be(ConnectionReasons.NotConnected, "a restart must not ask again for a sign-out that was cancelled");
        _inbox.SignOutPending.Should().BeFalse();
    }
}
