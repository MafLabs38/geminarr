using System.Net;
using System.Net.Sockets;
using Geminarr.Host.Http;

namespace Geminarr.Integration.Test;

/// <summary>The probe of the egress guard: a connection that is refused at once, in both families, says the guard is there; one that opens, or does not answer, says it is not.</summary>
public sealed class TcpEgressProbeFixture
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(2);

    private static IPEndPoint Closed(IPAddress address)
    {
        using var listener = new TcpListener(address, 0);
        listener.Start();
        return new IPEndPoint(address, ((IPEndPoint)listener.LocalEndpoint).Port);
    }

    private static bool HasIpv6Loopback()
    {
        try
        {
            using var listener = new TcpListener(IPAddress.IPv6Loopback, 0);
            listener.Start();
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    [Fact]
    public void should_say_refused_for_connections_that_are_refused_at_once_in_both_families()
    {
        Assert.SkipUnless(HasIpv6Loopback(), "this machine has no IPv6 loopback");

        new TcpEgressProbe(Closed(IPAddress.Loopback), Closed(IPAddress.IPv6Loopback), Wait).IsRefused().Should().BeTrue();
    }

    [Fact]
    public void should_not_say_refused_for_a_connection_that_opens_over_ipv4()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        new TcpEgressProbe((IPEndPoint)listener.LocalEndpoint, Closed(IPAddress.IPv6Loopback), Wait).IsRefused().Should().BeFalse("a connection that is let through is a guard that is not there");
    }

    [Fact]
    public void should_not_say_refused_when_only_the_ipv4_guard_is_there_and_ipv6_goes_through()
    {
        Assert.SkipUnless(HasIpv6Loopback(), "this machine has no IPv6 loopback");
        using var listener = new TcpListener(IPAddress.IPv6Loopback, 0);
        listener.Start();

        new TcpEgressProbe(Closed(IPAddress.Loopback), (IPEndPoint)listener.LocalEndpoint, Wait).IsRefused().Should().BeFalse("a missing guard of IPv6 is a missing guard");
    }
}
