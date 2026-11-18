using Geminarr.Core.Browser;

namespace Geminarr.Core.Test.Browser;

public sealed class DesktopAddressFixture
{
    [Theory]
    [InlineData(null, "http", "localhost", "http://localhost:8283")]
    [InlineData(null, "https", "192.168.1.10", "https://192.168.1.10:8283")]
    [InlineData("https://geminarr.example:8443", "http", "10.0.0.2", "https://geminarr.example:8283")]
    public void should_put_the_desktop_on_its_own_port_of_the_page_the_person_is_on(string? publicUrl, string scheme, string host, string expected)
    {
        DesktopAddress.Origin(new BrowserOptions(), publicUrl, scheme, host).Should().Be(expected);
    }

    [Fact]
    public void should_keep_to_the_address_that_a_proxy_gives_the_desktop()
    {
        var browser = new BrowserOptions { DesktopPublicUrl = "https://desktop.homelab.example/", DesktopPort = 9000 };

        DesktopAddress.Origin(browser, "https://geminarr.example", "http", "localhost").Should().Be("https://desktop.homelab.example");
    }

    [Fact]
    public void should_follow_the_port_that_is_set()
    {
        DesktopAddress.Origin(new BrowserOptions { DesktopPort = 9000 }, null, "https", "localhost").Should().Be("https://localhost:9000");
    }
}
