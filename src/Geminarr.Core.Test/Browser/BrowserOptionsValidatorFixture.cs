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
        new BrowserOptionsValidator().Validate(null, new BrowserOptions { CdpUrl = address }).Failed.Should().Be(!valid);
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
}
