using Geminarr.Core.Access;
using Geminarr.Core.Browser;

namespace Geminarr.Core.Test.Access;

public sealed class AuthOptionsBrowserValidatorFixture
{
    private static AuthOptionsBrowserValidator With(string? desktopUrl) => new(Microsoft.Extensions.Options.Options.Create(new BrowserOptions { DesktopUrl = desktopUrl }));

    [Theory]
    [InlineData(1)]
    [InlineData(11)]
    public void should_refuse_a_lightened_minimum_while_a_browser_is_configured_and_say_why(int minimum)
    {
        var result = With("http://127.0.0.1:3000").Validate(null, new AuthOptions { MinPasswordLength = minimum });

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("GEMINARR__AUTH__MINPASSWORDLENGTH").And.Contain("key to the browser's session");
    }

    [Theory]
    [InlineData(12)]
    [InlineData(64)]
    public void should_accept_the_strict_minimum_or_more_with_a_browser(int minimum)
    {
        With("http://127.0.0.1:3000").Validate(null, new AuthOptions { MinPasswordLength = minimum }).Succeeded.Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    public void should_let_a_sandbox_without_a_browser_lighten_the_minimum(string? desktopUrl)
    {
        With(desktopUrl).Validate(null, new AuthOptions { MinPasswordLength = 1 }).Succeeded.Should().BeTrue();
    }
}
