using Geminarr.Core.Access;
using Microsoft.Extensions.Time.Testing;

namespace Geminarr.Core.Test.Access;

public sealed class SignInServiceFixture
{
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 13, 8, 0, 0, TimeSpan.Zero));

    private (SignInService Service, SessionRegistry Sessions) Subject(IOperatorCredentials? credentials = null)
    {
        var sessions = new SessionRegistry(Options.Of(new AuthOptions()), _clock);
        return (new SignInService(credentials ?? new FakeCredentials(), new LoginThrottle(_clock), sessions), sessions);
    }

    [Fact]
    public void should_start_a_session_when_the_name_and_the_password_are_right()
    {
        var (service, sessions) = Subject();

        var outcome = service.SignIn("operator-name", "a long enough password");

        outcome.Status.Should().Be(SignInStatus.Signed);
        sessions.IsActive(outcome.SessionId).Should().BeTrue();
    }

    [Theory]
    [InlineData("operator-name", "wrong")]
    [InlineData("someone-else", "a long enough password")]
    [InlineData("someone-else", "wrong")]
    public void should_give_the_same_answer_for_a_wrong_name_and_a_wrong_password(string name, string password)
    {
        var (service, sessions) = Subject();

        var outcome = service.SignIn(name, password);

        outcome.Should().Be(new SignInOutcome(SignInStatus.Wrong));
        sessions.Count.Should().Be(0);
    }

    [Theory]
    [InlineData(null, "a long enough password")]
    [InlineData("", "a long enough password")]
    [InlineData("   ", "a long enough password")]
    [InlineData("operator-name", null)]
    [InlineData("operator-name", "")]
    public void should_refuse_a_missing_name_or_password_without_counting_it(string? name, string? password)
    {
        var (service, _) = Subject();

        for (var attempt = 0; attempt < 10; attempt++)
        {
            service.SignIn(name, password).Status.Should().Be(SignInStatus.Wrong);
        }

        service.SignIn("operator-name", "a long enough password").Status.Should().Be(SignInStatus.Signed);
    }

    [Fact]
    public void should_refuse_a_name_or_a_password_longer_than_any_real_one_without_counting_it()
    {
        var (service, _) = Subject();

        service.SignIn(new string('n', SignInService.MaxNameLength + 1), "a long enough password").Status.Should().Be(SignInStatus.Wrong);
        service.SignIn("operator-name", new string('p', SignInService.MaxPasswordLength + 1)).Status.Should().Be(SignInStatus.Wrong);
        service.SignIn("operator-name", "a long enough password").Status.Should().Be(SignInStatus.Signed);
    }

    [Fact]
    public void should_lock_the_name_after_five_failures_even_for_the_right_password_and_say_for_how_long()
    {
        var (service, _) = Subject();
        for (var attempt = 0; attempt < LoginThrottle.MaxFailures; attempt++)
        {
            service.SignIn("operator-name", "wrong").Status.Should().Be(SignInStatus.Wrong);
        }

        var outcome = service.SignIn("operator-name", "a long enough password");

        outcome.Status.Should().Be(SignInStatus.Locked);
        outcome.RetryAfter.Should().BeGreaterThan(TimeSpan.Zero).And.BeLessThanOrEqualTo(LoginThrottle.Window);
    }

    [Fact]
    public void should_let_the_operator_in_again_when_the_lockout_has_passed()
    {
        var (service, _) = Subject();
        for (var attempt = 0; attempt < LoginThrottle.MaxFailures; attempt++)
        {
            service.SignIn("operator-name", "wrong");
        }

        _clock.Advance(LoginThrottle.Window + TimeSpan.FromSeconds(1));

        service.SignIn("operator-name", "a long enough password").Status.Should().Be(SignInStatus.Signed);
    }

    [Fact]
    public void should_lock_a_made_up_name_like_the_real_one_so_that_a_lockout_says_nothing_about_the_name()
    {
        var (service, _) = Subject();
        for (var attempt = 0; attempt < LoginThrottle.MaxFailures; attempt++)
        {
            service.SignIn("someone-else", "wrong");
        }

        service.SignIn("someone-else", "wrong").Status.Should().Be(SignInStatus.Locked);
    }

    [Fact]
    public void should_never_sign_anybody_in_when_there_is_no_operator()
    {
        var (service, sessions) = Subject(new FakeCredentials(name: null, password: null, status: OperatorStatus.NotConfigured));

        service.SignIn("operator-name", "a long enough password").Status.Should().Be(SignInStatus.Wrong);
        sessions.Count.Should().Be(0);
    }

    [Fact]
    public void should_end_the_session_when_signing_out()
    {
        var (service, sessions) = Subject();
        var id = service.SignIn("operator-name", "a long enough password").SessionId;

        service.SignOut(id);

        sessions.IsActive(id).Should().BeFalse();
    }
}
