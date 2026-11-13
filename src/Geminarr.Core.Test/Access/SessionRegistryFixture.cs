using Geminarr.Core.Access;
using Microsoft.Extensions.Time.Testing;

namespace Geminarr.Core.Test.Access;

public sealed class SessionRegistryFixture
{
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 13, 8, 0, 0, TimeSpan.Zero));

    private SessionRegistry Subject(int hours = 12) => new(Options.Of(new AuthOptions { SessionHours = hours }), _clock);

    [Fact]
    public void should_start_a_session_that_nobody_can_guess_and_that_is_active()
    {
        var subject = Subject();

        var first = subject.Start();
        var second = subject.Start();

        first.Should().NotBe(second).And.MatchRegex("^[A-Za-z0-9_-]{43}$");
        subject.IsActive(first).Should().BeTrue();
    }

    [Fact]
    public void should_end_a_session_at_once_when_it_is_ended()
    {
        var subject = Subject();
        var id = subject.Start();

        subject.End(id);

        subject.IsActive(id).Should().BeFalse();
    }

    [Fact]
    public void should_end_a_session_at_its_absolute_lifetime_whether_it_was_used_or_not()
    {
        var subject = Subject(hours: 2);
        var id = subject.Start();

        _clock.Advance(TimeSpan.FromHours(2) - TimeSpan.FromSeconds(1));
        subject.IsActive(id).Should().BeTrue();

        _clock.Advance(TimeSpan.FromSeconds(1));
        subject.IsActive(id).Should().BeFalse();
        subject.Count.Should().Be(0);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-session")]
    public void should_not_know_a_session_that_was_never_started(string? id)
    {
        Subject().IsActive(id).Should().BeFalse();
    }

    [Fact]
    public void should_not_take_the_end_of_an_unknown_session_for_an_error()
    {
        var subject = Subject();
        var act = () => { subject.End(null); subject.End("unknown"); };

        act.Should().NotThrow();
    }

    [Fact]
    public void should_keep_at_most_a_fixed_number_of_sessions_and_drop_the_oldest()
    {
        var subject = Subject();
        var first = subject.Start();
        for (var session = 0; session < SessionRegistry.MaxSessions; session++)
        {
            _clock.Advance(TimeSpan.FromSeconds(1));
            subject.Start();
        }

        subject.Count.Should().Be(SessionRegistry.MaxSessions);
        subject.IsActive(first).Should().BeFalse();
    }
}
