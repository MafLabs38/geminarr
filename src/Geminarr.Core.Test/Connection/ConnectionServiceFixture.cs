using Geminarr.Core.Browser;
using Geminarr.Core.Connection;
using Geminarr.Core.Extension;
using Geminarr.Core.Live;
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

    private readonly RecordingAccount _accounts = new();

    public void Dispose() => _workspace.Dispose();

    private ConnectionService Subject(ReaderKind kind = ReaderKind.Web) => new(_tracker, _inbox, Options.Of(new ReaderOptions { Kind = kind }), _clock, _accounts);

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
        _accounts.Forgotten.Should().Be(1, "no account is the last one after a disconnect that was asked for: the machine of the one that was falls silent");
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
    public void should_ask_for_the_sign_out_again_after_a_restart_only_when_the_record_says_that_it_was_not_confirmed()
    {
        var pending = new ConnectionRecord(ConnectionState.NotConnected, _clock.GetUtcNow(), null, 0, ConnectionReasons.Disconnected, SignOutPending: true);

        var restored = ExtensionInbox.Restored(pending);

        restored.Disconnected.Should().BeTrue();
        restored.SignOutPending.Should().BeTrue();
        var confirmed = ExtensionInbox.Restored(pending with { SignOutPending = false });
        confirmed.Disconnected.Should().BeTrue("the disconnect stands until somebody signs in");
        confirmed.SignOutPending.Should().BeFalse("the extension had confirmed it: a person who signed in since is not signed out");
        ExtensionInbox.Restored(pending with { LastReason = ConnectionReasons.NotConnected, SignOutPending = false }).Disconnected.Should().BeFalse();
    }

    [Fact]
    public void should_write_that_the_sign_out_is_confirmed_when_the_extension_says_that_nobody_is_signed_in()
    {
        var inbox = ExtensionInbox.Restored(_tracker.Disconnected(), signOutSettled: stillPending => _tracker.SignOutSettled(stillPending));
        _tracker.Current().SignOutPending.Should().BeTrue();

        inbox.RecordSession(signedIn: true, "1.0.0", _clock.GetUtcNow());
        _tracker.Current().SignOutPending.Should().BeTrue("a word of signed in is not the confirmation");
        inbox.RecordSession(signedIn: false, "1.0.0", _clock.GetUtcNow());

        _tracker.Current().SignOutPending.Should().BeFalse();
        _tracker.Current().LastReason.Should().Be(ConnectionReasons.Disconnected, "the disconnect stands");
        ExtensionInbox.Restored(_tracker.Current()).SignOutPending.Should().BeFalse();
    }

    [Fact]
    public void should_tell_the_screens_at_each_move_of_the_connection_and_when_a_sign_in_begins_or_a_sign_out_is_asked_or_given_up()
    {
        var notifier = new RecordingNotifier();
        var tracker = Tracking.Of(new ConnectionStore(_workspace.Database, _clock), Options.Of(new ReaderOptions { Kind = ReaderKind.Web }), _clock, notifier);
        var inbox = ExtensionInbox.Restored(tracker.Current(), live: notifier);
        var subject = new ConnectionService(tracker, inbox, Options.Of(new ReaderOptions { Kind = ReaderKind.Web }), _clock, _accounts, notifier);

        subject.BeginSigningIn();
        notifier.Topics.Should().Equal(LiveTopics.Connection);
        notifier.Topics.Clear();

        subject.Disconnect();
        notifier.Topics.Should().Contain(LiveTopics.Connection).And.Contain(LiveTopics.Status, "the health says that the connection changed");
        notifier.Topics.Clear();

        subject.CancelDisconnect();
        notifier.Topics.Should().Contain(LiveTopics.Connection);
        notifier.Topics.Clear();

        inbox.RecordSession(signedIn: true, "1.0.0", _clock.GetUtcNow());
        notifier.Topics.Should().Equal(LiveTopics.Connection);
    }

    [Fact]
    public void should_not_forget_a_second_disconnect_that_comes_between_the_confirmation_and_its_writing()
    {
        var subject = Subject();
        subject.Disconnect();

        // The extension confirmed and the inbox released its lock; the operator disconnects again; then the confirmation is written.
        subject.Disconnect();
        _tracker.SignOutSettled(() => _inbox.SignOutPending);

        _tracker.Current().SignOutPending.Should().BeTrue("the second sign-out is asked and not confirmed");
    }

    [Fact]
    public void should_forget_the_whole_disconnect_when_the_operator_cancels_it_during_a_pause()
    {
        var subject = Subject();
        subject.Disconnect();
        subject.Pause(null);

        subject.CancelDisconnect();

        ExtensionInbox.Restored(subject.Current()).Disconnected.Should().BeFalse();
        subject.Resume();
        ExtensionInbox.Restored(subject.Current()).Disconnected.Should().BeFalse("not after the resume either");
        subject.Current().SignOutPending.Should().BeFalse();
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
