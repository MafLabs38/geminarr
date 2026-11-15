using Geminarr.Core.Browser;

namespace Geminarr.Core.Test.Browser;

public sealed class SigningInGuardFixture
{
    [Fact]
    public void should_step_aside_for_the_allowed_passes_in_a_row_and_then_look_at_the_session()
    {
        var subject = new SigningInGuard();

        subject.StepsAside(aTabIsOnASignInPage: true, allowedPasses: 2).Should().BeTrue();
        subject.StepsAside(true, 2).Should().BeTrue();
        subject.StepsAside(true, 2).Should().BeFalse("a tab that stays is not somebody typing");
        subject.StepsAside(true, 2).Should().BeFalse();
    }

    [Fact]
    public void should_start_counting_again_when_no_tab_is_on_a_sign_in_page()
    {
        var subject = new SigningInGuard();
        subject.StepsAside(true, 1);
        subject.StepsAside(true, 1).Should().BeFalse();

        subject.StepsAside(false, 1).Should().BeFalse("nobody is signing in");

        subject.StepsAside(true, 1).Should().BeTrue("a new sign-in has its own count");
    }

    [Fact]
    public void should_never_step_aside_when_no_pass_is_allowed_to()
    {
        new SigningInGuard().StepsAside(true, 0).Should().BeFalse();
    }

    [Fact]
    public void should_step_aside_again_for_a_new_sign_in_after_a_tab_that_stayed_too_long_is_closed()
    {
        var subject = new SigningInGuard();
        subject.StepsAside(true, 1);
        subject.StepsAside(true, 1).Should().BeFalse();
        subject.StepsAside(false, 1);

        subject.StepsAside(true, 1).Should().BeTrue();
    }
}
