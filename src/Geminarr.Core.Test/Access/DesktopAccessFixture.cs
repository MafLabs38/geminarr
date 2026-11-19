using Geminarr.Core.Access;
using Microsoft.Extensions.Time.Testing;

namespace Geminarr.Core.Test.Access;

public sealed class DesktopAccessFixture
{
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 17, 12, 0, 0, TimeSpan.Zero));
    private readonly SessionRegistry _sessions;
    private readonly DesktopAccess _subject;

    public DesktopAccessFixture()
    {
        _sessions = new SessionRegistry(Microsoft.Extensions.Options.Options.Create(new AuthOptions()), _clock);
        _subject = new DesktopAccess(_sessions, _clock);
    }

    [Fact]
    public void should_exchange_a_ticket_for_a_cookie_that_stands_for_the_session_that_asked()
    {
        var session = _sessions.Start();

        var token = _subject.Redeem(_subject.IssueTicket(session));

        token.Should().NotBeNullOrEmpty();
        _subject.SessionOf(token).Should().Be(session);
    }

    [Fact]
    public void should_give_no_ticket_to_a_session_that_is_not_open()
    {
        _subject.IssueTicket(null).Should().BeNull();
        _subject.IssueTicket("unknown").Should().BeNull();
        var ended = _sessions.Start();
        _sessions.End(ended);

        _subject.IssueTicket(ended).Should().BeNull();
    }

    [Fact]
    public void should_use_a_ticket_once()
    {
        var ticket = _subject.IssueTicket(_sessions.Start());

        _subject.Redeem(ticket).Should().NotBeNull();
        _subject.Redeem(ticket).Should().BeNull();
    }

    [Fact]
    public void should_not_exchange_a_ticket_after_a_minute()
    {
        var ticket = _subject.IssueTicket(_sessions.Start());

        _clock.Advance(DesktopAccess.TicketLifetime + TimeSpan.FromSeconds(1));

        _subject.Redeem(ticket).Should().BeNull();
    }

    [Fact]
    public void should_not_exchange_a_ticket_whose_session_ended_and_end_the_cookie_with_the_session()
    {
        var first = _sessions.Start();
        var orphanTicket = _subject.IssueTicket(first);
        var token = _subject.Redeem(_subject.IssueTicket(first));
        _sessions.End(first);

        _subject.Redeem(orphanTicket).Should().BeNull();
        _subject.SessionOf(token).Should().BeNull("signing out ends the desktop's cookie too");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("a-token-that-was-never-given")]
    public void should_know_no_session_for_a_cookie_that_was_never_given(string? token)
    {
        _subject.SessionOf(token).Should().BeNull();
        _subject.Redeem(token).Should().BeNull();
    }

    [Fact]
    public void should_keep_a_bounded_number_of_tickets_and_cookies_and_drop_the_oldest_first()
    {
        var session = _sessions.Start();
        var first = _subject.IssueTicket(session);
        for (var i = 0; i < DesktopAccess.MaxEntries; i++)
        {
            _clock.Advance(TimeSpan.FromMilliseconds(1));
            _subject.IssueTicket(session);
        }

        _subject.Redeem(first).Should().BeNull("a flood of tickets does not keep them all in memory");

        var tokens = new List<string?>();
        for (var i = 0; i < DesktopAccess.MaxEntries + 1; i++)
        {
            _clock.Advance(TimeSpan.FromMilliseconds(1));
            tokens.Add(_subject.Redeem(_subject.IssueTicket(session)));
        }

        _subject.SessionOf(tokens[0]).Should().BeNull("the oldest cookie made room for the newest");
        _subject.SessionOf(tokens[^1]).Should().Be(session);
    }

    [Fact]
    public void should_not_let_tickets_that_expired_take_the_room_of_one_that_is_valid()
    {
        var session = _sessions.Start();
        for (var i = 0; i < DesktopAccess.MaxEntries; i++)
        {
            _subject.IssueTicket(session);
        }

        _clock.Advance(DesktopAccess.TicketLifetime + TimeSpan.FromSeconds(1));
        var valid = _subject.IssueTicket(session);

        _subject.Redeem(valid).Should().NotBeNull("the stale tickets were swept, they do not count against the limit");
    }

    [Fact]
    public void should_sweep_the_cookies_of_the_sessions_that_ended_when_a_new_one_is_given()
    {
        var gone = _sessions.Start();
        var token = _subject.Redeem(_subject.IssueTicket(gone));
        _sessions.End(gone);
        var open = _sessions.Start();

        var fresh = _subject.Redeem(_subject.IssueTicket(open));

        _subject.SessionOf(token).Should().BeNull();
        _subject.SessionOf(fresh).Should().Be(open);
    }
}
