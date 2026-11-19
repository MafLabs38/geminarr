using Geminarr.Host.Http;
using Microsoft.AspNetCore.Http;

namespace Geminarr.Integration.Test;

/// <summary>Whose page opens a WebSocket to the desktop: the rule that tells the person's own page from a hostile one that the browser or another site loads.</summary>
public sealed class BrowserRelayOriginFixture
{
    private static bool FromAnother(string? origin, string host, string? desktopPublicUrl = null) =>
        RelayRules.IsFromAnotherOrigin(origin, new HostString(host), desktopPublicUrl);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void should_take_a_request_that_names_no_origin_for_no_page(string? origin)
    {
        FromAnother(origin, "localhost:8283").Should().BeFalse();
    }

    [Theory]
    [InlineData("null")]
    [InlineData("not an origin")]
    [InlineData("localhost")]
    public void should_take_an_origin_that_is_not_one_for_a_foreign_one(string origin)
    {
        FromAnother(origin, "localhost:8283").Should().BeTrue("a sandboxed frame says null");
    }

    [Theory]
    [InlineData("http://localhost:8283", "localhost:8283", false)]
    [InlineData("https://localhost:8283", "localhost:8283", false)]
    [InlineData("http://LOCALHOST:8283", "localhost:8283", false)]
    [InlineData("http://localhost:8282", "localhost:8283", true, "the operator's page is another origin: another port")]
    [InlineData("http://localhost:9000", "localhost:8283", true)]
    [InlineData("http://localhost", "localhost:8283", true)]
    [InlineData("http://localhost", "localhost", false)]
    [InlineData("https://localhost", "localhost", false)]
    [InlineData("http://localhost:9000", "localhost", true)]
    [InlineData("https://evil.example", "localhost:8283", true)]
    public void should_judge_a_direct_access_by_the_host_and_the_port_of_the_request(string origin, string host, bool foreign, string? because = null)
    {
        FromAnother(origin, host).Should().Be(foreign, because);
    }

    [Theory]
    [InlineData("https://desktop.homelab.example", "desktop.homelab.example", false)]
    [InlineData("https://desktop.homelab.example:8443", "desktop.internal:8283", false, "a proxy that sends a Host of its own")]
    [InlineData("http://desktop.homelab.example:443", "desktop.homelab.example", true, "the scheme is the desktop's too")]
    [InlineData("https://desktop.homelab.example:9000", "desktop.homelab.example", true)]
    [InlineData("https://geminarr.homelab.example", "desktop.homelab.example", true, "the operator's page is another origin, and so are its names")]
    [InlineData("https://evil.example", "desktop.homelab.example", true)]
    public void should_judge_a_websocket_by_the_desktops_public_address(string origin, string host, bool foreign, string? because = null)
    {
        // The proxy gives the desktop an address of its own, on port 8443 for the second row.
        var publicUrl = origin.Contains(":8443", StringComparison.Ordinal) ? "https://desktop.homelab.example:8443" : "https://desktop.homelab.example";

        FromAnother(origin, host, publicUrl).Should().Be(foreign, because);
    }
}
