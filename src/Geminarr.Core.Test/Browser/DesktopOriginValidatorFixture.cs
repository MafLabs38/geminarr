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

    [Fact]
    public void should_refuse_a_desktop_port_that_would_put_the_desktop_on_the_pages_own_origin()
    {
        var result = With("https://geminarr.homelab.example:8283").Validate(null, new BrowserOptions());

        result.Failed.Should().BeTrue("the address that people are given would be the page's: the desktop's port is the page's");
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
