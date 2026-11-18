using Geminarr.Core.Browser;

namespace Geminarr.Core.Test.Browser;

/// <summary>The rules of the settings of the browser: what is refused at start rather than found out in a pass.</summary>
public sealed class BrowserOptionsValidatorFixture
{
    [Theory]
    [InlineData("*.com")]
    [InlineData("static.example;*.org")]
    [InlineData("")]
    public void should_refuse_a_list_of_sign_in_hosts_that_is_empty_or_covers_a_whole_public_suffix(string hosts)
    {
        new BrowserOptionsValidator().Validate(null, new BrowserOptions { SignInHosts = hosts }).Failed.Should().BeTrue();
    }

    [Theory]
    [InlineData("https://accounts.google.com/ServiceLogin?continue=x", true)]
    [InlineData("http://accounts.google.com/ServiceLogin", false)]
    [InlineData("https://elsewhere.example/signin", false)]
    [InlineData("not an address", false)]
    [InlineData("", false)]
    [InlineData("https://user:secret@accounts.google.com/x", false)]
    public void should_accept_a_sign_in_page_only_on_a_sign_in_host_and_over_https(string address, bool valid)
    {
        new BrowserOptionsValidator().Validate(null, new BrowserOptions { SignInUrl = address }).Failed.Should().Be(!valid);
    }

    [Theory]
    [InlineData("javascript://accounts.google.com/%0Aalert(1)")]
    [InlineData("file://accounts.google.com/etc/passwd")]
    [InlineData("chrome://accounts.google.com/")]
    public void should_refuse_a_sign_in_page_that_is_not_http_or_https_even_with_the_sandbox_switch(string address)
    {
        new BrowserOptionsValidator().Validate(null, new BrowserOptions { SignInUrl = address, AllowInsecureAssistant = true }).Failed.Should().BeTrue();
    }

    [Fact]
    public void should_accept_plain_http_for_the_sign_in_page_when_the_sandbox_switch_allows_it()
    {
        new BrowserOptionsValidator().Validate(null, new BrowserOptions { SignInUrl = "http://accounts.google.com/x", AllowInsecureAssistant = true }).Failed.Should().BeFalse();
    }

    [Theory]
    [InlineData("http://127.0.0.1:9222", true)]
    [InlineData("http://localhost:9222", true)]
    [InlineData("http://[::1]:9222", true)]
    [InlineData("", true)]
    [InlineData("http://browser:9222", false)]
    [InlineData("http://10.0.0.5:9222", false)]
    [InlineData("ws://127.0.0.1:9222", false)]
    [InlineData("not an address", false)]
    [InlineData("http://user:secret@127.0.0.1:9222", false)]
    public void should_accept_only_a_loopback_address_for_the_debugging_port_of_the_browser(string address, bool valid)
    {
        new BrowserOptionsValidator().Validate(null, new BrowserOptions { DesktopUrl = address }).Failed.Should().Be(!valid);
    }

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

    [Theory]
    [InlineData(4)]
    [InlineData(301)]
    public void should_refuse_a_time_limit_out_of_range(int seconds)
    {
        new BrowserOptionsValidator().Validate(null, new BrowserOptions { SessionCheckTimeoutSeconds = seconds }).Failed.Should().BeTrue();
    }

    [Fact]
    public void should_accept_the_defaults()
    {
        new BrowserOptionsValidator().Validate(null, new BrowserOptions()).Succeeded.Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void should_refuse_a_number_of_passes_out_of_range_for_a_person_signing_in(int passes)
    {
        new BrowserOptionsValidator().Validate(null, new BrowserOptions { SigningInPasses = passes }).Failed.Should().BeTrue();
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
