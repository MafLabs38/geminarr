using Geminarr.Core.Connection;
using Microsoft.Extensions.Time.Testing;

namespace Geminarr.Core.Test.Connection;

public sealed class SignInObserverFixture
{
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 17, 9, 0, 0, TimeSpan.Zero));

    [Fact]
    public void should_say_nothing_ended_when_no_sign_in_was_ever_seen()
    {
        var subject = new SignInObserver(_clock);

        subject.Set(false);

        subject.SigningIn.Should().BeFalse();
        subject.EndedWithin(TimeSpan.FromHours(1)).Should().BeFalse("a sign-in that was never seen did not end");
    }

    [Fact]
    public void should_remember_when_a_sign_in_ended_and_forget_it_after_the_span()
    {
        var subject = new SignInObserver(_clock);
        subject.Set(true);
        subject.EndedWithin(TimeSpan.FromSeconds(15)).Should().BeFalse("it is under way, not ended");

        subject.Set(false);
        _clock.Advance(TimeSpan.FromSeconds(14));

        subject.EndedWithin(TimeSpan.FromSeconds(15)).Should().BeTrue();
        _clock.Advance(TimeSpan.FromSeconds(2));
        subject.EndedWithin(TimeSpan.FromSeconds(15)).Should().BeFalse();
    }
}
