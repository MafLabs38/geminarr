using Geminarr.Host.Http;

namespace Geminarr.Integration.Test;

/// <summary>Where the desktop's own origin listens: the interface's address with the desktop's port, and nothing without a desktop.</summary>
public sealed class DesktopListenerFixture
{
    private const string Desktop = "http://127.0.0.1:3000";

    [Theory]
    [InlineData("http://+:8282", null, "http://+:8282;http://+:8283")]
    [InlineData("http://+:8282/", null, "http://+:8282/;http://+:8283")]
    [InlineData("http://127.0.0.1:8282", null, "http://127.0.0.1:8282;http://127.0.0.1:8283", "the desktop is not wider than the page")]
    [InlineData("https://+:8443;http://+:8080", "9000", "https://+:8443;http://+:8080;https://+:9000")]
    [InlineData("http://+:8282;http://+:8283", null, "http://+:8282;http://+:8283", "already there")]
    public void should_add_the_desktops_port_on_the_interface_of_the_page(string urls, string? port, string expected, string? because = null)
    {
        DesktopListener.Plan(urls, Desktop, port).Should().Be(expected, because);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void should_add_nothing_without_a_desktop(string? desktop)
    {
        DesktopListener.Plan("http://+:8282", desktop, "9000").Should().Be("http://+:8282");
    }

    [Theory]
    [InlineData(null, null, "no address to listen on")]
    [InlineData("http://+:8283", null, "port of the interface")]
    [InlineData("http://+:8283/", null, "port of the interface")]
    [InlineData("http://+:8282", "0", "must be a port number")]
    [InlineData("http://+:8282", "65536", "must be a port number")]
    [InlineData("http://+:8282", "eight", "must be a port number")]
    public void should_refuse_a_desktop_that_would_never_open_and_say_which_setting(string? urls, string? port, string message)
    {
        var plan = () => DesktopListener.Plan(urls, Desktop, port);

        plan.Should().Throw<InvalidOperationException>().WithMessage($"*{message}*");
    }
}
