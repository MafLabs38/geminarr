using Geminarr.Core.Browser;

namespace Geminarr.Core.Test.Browser;

/// <summary>The rules of the settings of the browser: what is refused at start rather than found out in a pass.</summary>
public sealed class BrowserOptionsValidatorFixture
{
    [Theory]
    [InlineData("http://127.0.0.1:3000", true)]
    [InlineData("http://localhost:3000/", true)]
    [InlineData("", true)]
    [InlineData("https://browser.homelab.example/", false)]
    [InlineData("http://browser:3000", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("http://user:secret@127.0.0.1:3000", false)]
    public void should_accept_only_a_loopback_address_for_the_web_desktop_of_the_browser(string address, bool valid)
    {
        new BrowserOptionsValidator().Validate(null, new BrowserOptions { DesktopUrl = address }).Failed.Should().Be(!valid);
    }

    [Fact]
    public void should_accept_the_defaults()
    {
        new BrowserOptionsValidator().Validate(null, new BrowserOptions()).Succeeded.Should().BeTrue();
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("9223", true)]
    [InlineData("9223;9224", true)]
    [InlineData("0", false)]
    [InlineData("65536", false)]
    [InlineData("web", false)]
    public void should_accept_port_numbers_only_for_the_ports_a_test_may_open(string ports, bool valid)
    {
        new BrowserOptionsValidator().Validate(null, new BrowserOptions { AllowedOpenPorts = ports }).Failed.Should().Be(!valid);
    }

    [Theory]
    [InlineData("3000")]
    [InlineData("3001")]
    [InlineData("15001;3000")]
    public void should_never_tolerate_the_port_of_the_desktop(string ports)
    {
        var options = new BrowserOptions { DesktopUrl = "http://127.0.0.1:3000", AllowedOpenPorts = ports };

        new BrowserOptionsValidator().Validate(null, options).Failed.Should().BeTrue();
    }

    [Fact]
    public void should_tolerate_the_port_of_a_mesh_proxy()
    {
        var options = new BrowserOptions { DesktopUrl = "http://127.0.0.1:3000", AllowedOpenPorts = "15001;15006;15021" };

        new BrowserOptionsValidator().Validate(null, options).Succeeded.Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65_536)]
    public void should_refuse_a_desktop_port_that_is_not_a_port(int port)
    {
        new BrowserOptionsValidator().Validate(null, new BrowserOptions { DesktopPort = port }).Failed.Should().BeTrue();
    }

    [Theory]
    [InlineData("https://desktop.homelab.example", true)]
    [InlineData("https://desktop.homelab.example:8443/", true)]
    [InlineData("https://desktop.homelab.example/browser/", false)]
    [InlineData("https://desktop.homelab.example/?x=1", false)]
    [InlineData("https://user:pw@desktop.homelab.example", false)]
    [InlineData("not an address", false)]
    public void should_take_a_public_address_of_the_desktop_that_is_an_origin_and_nothing_else(string address, bool valid)
    {
        new BrowserOptionsValidator().Validate(null, new BrowserOptions { DesktopPublicUrl = address }).Succeeded.Should().Be(valid);
    }
}
