using Geminarr.Core.Access;

namespace Geminarr.Core.Test.Access;

public sealed class AuthOptionsValidatorFixture
{
    private readonly AuthOptionsValidator _subject = new();

    [Fact]
    public void should_accept_no_operator_at_all_and_the_defaults()
    {
        _subject.Validate(null, new AuthOptions()).Succeeded.Should().BeTrue();
    }

    [Fact]
    public void should_accept_a_name_with_its_password_file()
    {
        _subject.Validate(null, new AuthOptions { AdminUser = "operator-name", AdminPasswordFile = "/run/secrets/password" }).Succeeded.Should().BeTrue();
    }

    [Theory]
    [InlineData("operator-name", null)]
    [InlineData(null, "/run/secrets/password")]
    [InlineData("operator-name", " ")]
    public void should_refuse_a_name_without_its_file_and_the_other_way_round(string? name, string? file)
    {
        var result = _subject.Validate(null, new AuthOptions { AdminUser = name, AdminPasswordFile = file });

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("GEMINARR__AUTH__ADMINUSER").And.Contain("GEMINARR__AUTH__ADMINPASSWORDFILE");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(AuthOptions.MaxSessionHours + 1)]
    public void should_refuse_a_session_lifetime_outside_the_bounds_and_name_the_setting(int hours)
    {
        var result = _subject.Validate(null, new AuthOptions { SessionHours = hours });

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("GEMINARR__AUTH__SESSIONHOURS");
    }
}
