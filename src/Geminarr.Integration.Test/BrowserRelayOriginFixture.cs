using Geminarr.Core.Access;
using Geminarr.Core.Configuration;
using Geminarr.Host.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Geminarr.Integration.Test;

/// <summary>Whose page opens a WebSocket to the desktop: the rule that tells the person's own page from a hostile one that the browser or another site loads.</summary>
public sealed class BrowserRelayOriginFixture
{
    private static bool FromAnother(string? origin, string host, string allowed = "localhost", string? publicUrl = null) =>
        BrowserRelay.IsFromAnotherOrigin(origin, new HostString(host), new AllowedHostList(Options.Create(new GeminarrOptions { AllowedHostNames = allowed, PublicUrl = publicUrl })), publicUrl);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void should_take_a_request_that_names_no_origin_for_no_page(string? origin)
    {
        FromAnother(origin, "localhost:8282").Should().BeFalse();
    }

    [Theory]
    [InlineData("null")]
    [InlineData("not an origin")]
    [InlineData("localhost")]
    public void should_take_an_origin_that_is_not_one_for_a_foreign_one(string origin)
    {
        FromAnother(origin, "localhost:8282").Should().BeTrue("a sandboxed frame says null");
    }

    [Theory]
    [InlineData("http://localhost:8282", "localhost:8282", false)]
    [InlineData("https://localhost:8282", "localhost:8282", false)]
    [InlineData("http://LOCALHOST:8282", "localhost:8282", false)]
    [InlineData("http://localhost:9000", "localhost:8282", true)]
    [InlineData("http://localhost", "localhost:8282", true)]
    [InlineData("http://localhost", "localhost", false)]
    [InlineData("https://localhost", "localhost", false)]
    [InlineData("http://localhost:9000", "localhost", true)]
    [InlineData("https://evil.example", "localhost:8282", true)]
    public void should_judge_a_direct_access_by_the_host_and_the_port_of_the_request(string origin, string host, bool foreign)
    {
        FromAnother(origin, host).Should().Be(foreign);
    }

    [Theory]
    [InlineData("https://geminarr.example:8443", false)]
    [InlineData("https://geminarr.example:9000", true)]
    [InlineData("https://geminarr.example", false)]
    [InlineData("https://evil.example:8443", true)]
    public void should_take_the_public_address_for_the_page_even_when_the_proxy_sends_no_port(string origin, bool foreign)
    {
        // A proxy on port 8443 that sends `Host: geminarr.example` without the port.
        FromAnother(origin, "geminarr.example", allowed: "localhost", publicUrl: "https://geminarr.example:8443").Should().Be(foreign);
    }

    [Theory]
    [InlineData("https://localhost", false)]
    [InlineData("https://geminarr.example", false)]
    [InlineData("http://localhost:9000", true)]
    [InlineData("https://evil.example", true)]
    public void should_judge_the_origin_by_the_names_the_page_may_be_reached_by_when_a_proxy_sends_a_host_of_its_own(string origin, bool foreign)
    {
        FromAnother(origin, "geminarr.internal:8282", allowed: "localhost;geminarr.example").Should().Be(foreign);
    }

    [Fact]
    public void should_not_judge_by_a_list_that_says_nothing()
    {
        FromAnother("https://other.example", "geminarr.internal:8282", allowed: "*").Should().BeTrue("with every name allowed, a name proves nothing");
        FromAnother("http://geminarr.internal:8282", "geminarr.internal:8282", allowed: "*").Should().BeFalse("the page's own host and port still do");
    }
}
