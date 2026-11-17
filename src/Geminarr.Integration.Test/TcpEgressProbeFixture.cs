using System.Net;
using System.Net.Sockets;
using Geminarr.Host.Http;

namespace Geminarr.Integration.Test;

/// <summary>The probe of the egress guard: a connection that is refused at once says the guard is there; one that opens, or does not answer, says it is not.</summary>
public sealed class TcpEgressProbeFixture
{
    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    [Fact]
    public void should_say_refused_for_a_connection_that_is_refused_at_once()
    {
        new TcpEgressProbe(new IPEndPoint(IPAddress.Loopback, FreePort()), TimeSpan.FromSeconds(2)).IsRefused().Should().BeTrue();
    }

    [Fact]
    public void should_not_say_refused_for_a_connection_that_opens()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        new TcpEgressProbe((IPEndPoint)listener.LocalEndpoint, TimeSpan.FromSeconds(2)).IsRefused().Should().BeFalse("a connection that is let through is a guard that is not there");
    }

    [Fact]
    public void should_not_say_refused_for_an_address_that_has_no_route_or_does_not_answer()
    {
        // The reserved documentation address that the real probe uses: nothing routes it, so what comes back is anything but a refusal at once (it is a refusal only behind the guard).
        var refused = new TcpEgressProbe(new IPEndPoint(IPAddress.Parse("192.0.2.1"), 9), TimeSpan.FromMilliseconds(300)).IsRefused();

        refused.Should().BeFalse("a test machine has no egress guard");
    }
}
