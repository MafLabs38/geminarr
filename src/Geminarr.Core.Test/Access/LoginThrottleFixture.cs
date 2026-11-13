using Geminarr.Core.Access;
using Microsoft.Extensions.Time.Testing;

namespace Geminarr.Core.Test.Access;

public sealed class LoginThrottleFixture
{
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 13, 8, 0, 0, TimeSpan.Zero));

    private LoginThrottle Subject() => new(_clock);

    [Fact]
    public void should_refuse_the_attempt_after_five_failures_and_say_for_how_long()
    {
        var subject = Subject();
        for (var attempt = 0; attempt < LoginThrottle.MaxFailures; attempt++)
        {
            subject.TryReserve("admin", isOperator: true).Should().BeTrue();
            _clock.Advance(TimeSpan.FromMinutes(1));
        }

        subject.TryReserve("admin", isOperator: true).Should().BeFalse();
        subject.LockedFor("admin").Should().Be(TimeSpan.FromMinutes(10));
    }

    [Fact]
    public void should_let_the_name_try_again_once_the_first_failure_has_aged_out()
    {
        var subject = Subject();
        for (var attempt = 0; attempt < LoginThrottle.MaxFailures; attempt++)
        {
            subject.TryReserve("admin", isOperator: true);
        }

        _clock.Advance(LoginThrottle.Window + TimeSpan.FromSeconds(1));

        subject.TryReserve("admin", isOperator: true).Should().BeTrue();
        subject.LockedFor("admin").Should().BeNull();
    }

    [Fact]
    public void should_start_again_from_nothing_when_an_attempt_succeeds()
    {
        var subject = Subject();
        for (var attempt = 0; attempt < LoginThrottle.MaxFailures - 1; attempt++)
        {
            subject.TryReserve("admin", isOperator: true);
        }

        subject.Succeeded("admin");

        Enumerable.Range(0, LoginThrottle.MaxFailures).Should().OnlyContain(_ => subject.TryReserve("admin", isOperator: true));
    }

    [Fact]
    public void should_count_each_name_apart_and_ignore_the_case_of_the_name()
    {
        var subject = Subject();
        for (var attempt = 0; attempt < LoginThrottle.MaxFailures; attempt++)
        {
            subject.TryReserve("Admin", isOperator: true);
        }

        subject.TryReserve("ADMIN", isOperator: true).Should().BeFalse();
        subject.TryReserve("someone-else", isOperator: false).Should().BeTrue();
    }

    [Fact]
    public void should_make_room_for_a_new_name_by_forgetting_the_made_up_ones_and_never_the_operator()
    {
        var subject = Subject();
        for (var attempt = 0; attempt < LoginThrottle.MaxFailures; attempt++)
        {
            subject.TryReserve("admin", isOperator: true);
        }

        for (var guess = 0; guess < LoginThrottle.MaxTracked + 50; guess++)
        {
            subject.TryReserve($"guess-{guess}", isOperator: false);
            _clock.Advance(TimeSpan.FromMilliseconds(1));
        }

        subject.TryReserve("a-new-guess", isOperator: false).Should().BeTrue();
        subject.LockedFor("admin").Should().NotBeNull("a flood of made-up names must not lift the lockout of the operator");
    }

    [Fact]
    public void should_not_say_a_name_is_locked_when_it_was_never_seen()
    {
        Subject().LockedFor("nobody").Should().BeNull();
    }
}
