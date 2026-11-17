using Geminarr.Core.Browser;
using Geminarr.Core.Configuration;

namespace Geminarr.Core.Test.Browser;

public sealed class DesktopOriginValidatorFixture
{
    private static DesktopOriginValidator With(string? publicUrl) => new(Microsoft.Extensions.Options.Options.Create(new GeminarrOptions { PublicUrl = publicUrl }));

    [Theory]
    [InlineData("https://geminarr.homelab.example", "https://geminarr.homelab.example", true)]
    [InlineData("https://geminarr.homelab.example:443", "https://GEMINARR.homelab.example/", true)]
    [InlineData("https://desktop.homelab.example", "https://geminarr.homelab.example", false)]
    [InlineData("https://geminarr.homelab.example:8443", "https://geminarr.homelab.example", false)]
    [InlineData("http://geminarr.homelab.example", "https://geminarr.homelab.example", false)]
    public void should_refuse_a_desktop_that_has_the_address_of_the_page(string desktop, string page, bool refused)
    {
        var result = With(page).Validate(null, new BrowserOptions { DesktopPublicUrl = desktop });

        result.Failed.Should().Be(refused);
        if (refused)
        {
            result.FailureMessage.Should().Contain("GEMINARR__BROWSER__DESKTOPPUBLICURL").And.Contain("GEMINARR__PUBLICURL");
        }
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("https://desktop.homelab.example", null)]
    [InlineData(null, "https://geminarr.homelab.example")]
    public void should_accept_what_does_not_name_both(string? desktop, string? page)
    {
        With(page).Validate(null, new BrowserOptions { DesktopPublicUrl = desktop }).Succeeded.Should().BeTrue();
    }
}
