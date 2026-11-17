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

        new TcpEgressProbe(Closed(IPAddress.Loopback), Closed(IPAddress.IPv6Loopback), Wait, _ => true).IsRefused().Should().BeTrue();
    }

    [Fact]
    public void should_not_say_refused_for_a_connection_that_opens_over_ipv4()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        new TcpEgressProbe((IPEndPoint)listener.LocalEndpoint, Closed(IPAddress.IPv6Loopback), Wait, _ => true).IsRefused().Should().BeFalse("a connection that is let through is a guard that is not there");
    }

    [Fact]
    public void should_not_say_refused_when_only_the_ipv4_guard_is_there_and_ipv6_goes_through()
    {
        Assert.SkipUnless(HasIpv6Loopback(), "this machine has no IPv6 loopback");
        using var listener = new TcpListener(IPAddress.IPv6Loopback, 0);
        listener.Start();

        new TcpEgressProbe(Closed(IPAddress.Loopback), (IPEndPoint)listener.LocalEndpoint, Wait, _ => true).IsRefused().Should().BeFalse("a missing guard of IPv6 is a missing guard");
    }

    [Fact]
    public void should_say_nothing_to_guard_for_a_family_that_has_no_default_route_and_not_for_one_that_has()
    {
        using var open = new TcpListener(IPAddress.Loopback, 0);
        open.Start();
        var v4Open = (IPEndPoint)open.LocalEndpoint;
        var v6Closed = new IPEndPoint(IPAddress.IPv6Loopback, 9);

        new TcpEgressProbe(v4Open, v6Closed, Wait, family => family != AddressFamily.InterNetwork).IsRefused().Should().BeTrue("a family without a route cannot be used: there is nothing to guard in it");
        new TcpEgressProbe(v4Open, v6Closed, Wait, _ => true).IsRefused().Should().BeFalse("a family with a route that lets the connection through is a guard that is missing");
    }

    [Theory]
    [InlineData("Iface\tDestination\tGateway \tFlags\tRefCnt\tUse\tMetric\tMask\n" + "eth0\t00000000\t0102A8C0\t0003\t0\t0\t0\t00000000\n", true)]
    [InlineData("Iface\tDestination\tGateway \tFlags\tRefCnt\tUse\tMetric\tMask\n" + "eth0\t0002A8C0\t00000000\t0001\t0\t0\t0\t00FFFFFF\n", false)]
    public void should_read_a_default_route_in_the_table_of_ipv4(string table, bool expected)
    {
        var file = Path.Combine(Path.GetTempPath(), $"geminarr-route-{Guid.NewGuid():N}");
        File.WriteAllText(file, table);
        try
        {
            RoutingTable.HasDefaultRouteV4(file).Should().Be(expected);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Theory]
    [InlineData("00000000000000000000000000000000 00 00000000000000000000000000000000 00 fe800000000000000000000000000001 00000064 00000001 00000000 00000003 eth0\n", true)]
    [InlineData("00000000000000000000000000000000 00 00000000000000000000000000000000 00 00000000000000000000000000000000 ffffffff 00000001 00000000 00200200 lo\n", false)]
    [InlineData("fe800000000000000000000000000000 40 00000000000000000000000000000000 00 00000000000000000000000000000000 00000100 00000001 00000000 00000001 eth0\n", false)]
    public void should_read_a_default_route_in_the_table_of_ipv6_and_not_the_one_that_says_unreachable(string table, bool expected)
    {
        var file = Path.Combine(Path.GetTempPath(), $"geminarr-route6-{Guid.NewGuid():N}");
        File.WriteAllText(file, table);
        try
        {
            RoutingTable.HasDefaultRouteV6(file).Should().Be(expected);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void should_take_a_table_that_cannot_be_read_for_a_route()
    {
        RoutingTable.HasDefaultRouteV4(Path.Combine(Path.GetTempPath(), "geminarr-no-such-table")).Should().BeTrue("what cannot be read is not taken for nothing to guard");
    }
}
