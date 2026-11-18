using System.Net;
using System.Net.Sockets;
using Geminarr.Core.Browser;

namespace Geminarr.Host.Http;

/// <summary>
/// The probe of the egress guard (ADR 0020), for both families: a connection to an address of the range that is reserved for documentation (<c>192.0.2.1</c> and <c>2001:db8::1</c>, which no network routes) is refused at once by the
/// guard (a reset), and otherwise waits for a route that is not there, finds none, or goes out. Nothing is sent: the connection is opened and dropped. It measures the property that matters (an address on no list is refused to
/// Geminarr's process), not the way that it is done. Where a family has no default route at all (read in the routing table of the namespace, not guessed from an error: a router can send the same error), its guard has nothing to say: Geminarr could not use it either.
/// </summary>
public sealed class TcpEgressProbe : IEgressProbe
{
    private readonly IPEndPoint _v4;
    private readonly IPEndPoint _v6;
    private readonly int _waitMicroseconds;
    private readonly Func<AddressFamily, bool> _hasDefaultRoute;

    public TcpEgressProbe()
        : this(new IPEndPoint(IPAddress.Parse("192.0.2.1"), 9), new IPEndPoint(IPAddress.Parse("2001:db8::1"), 9), TimeSpan.FromMilliseconds(1_000), RoutingTable.HasDefaultRoute)
    {
    }

    public TcpEgressProbe(IPEndPoint v4, IPEndPoint v6, TimeSpan wait, Func<AddressFamily, bool> hasDefaultRoute)
    {
        _v4 = v4;
        _v6 = v6;
        _waitMicroseconds = (int)(wait.TotalMilliseconds * 1_000);
        _hasDefaultRoute = hasDefaultRoute;
    }

    public bool IsRefused() => Outcome(_v4) && Outcome(_v6);

    /// <summary>A family with no default route cannot be used by Geminarr: there is nothing to guard. Otherwise only a refusal at once is the guard: any other answer (a wait, an error that a router can send too) is not.</summary>
    private bool Outcome(IPEndPoint target)
    {
        if (!_hasDefaultRoute(target.AddressFamily))
        {
            return true;
        }

        try
        {
            using var socket = new Socket(target.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { Blocking = false };
            try
            {
                socket.Connect(target);
                return false;
            }
            catch (SocketException ex) when (ex.SocketErrorCode is SocketError.WouldBlock or SocketError.InProgress)
            {
                // The answer comes later: waited for below.
            }

            if (!socket.Poll(_waitMicroseconds, SelectMode.SelectWrite) && !socket.Poll(0, SelectMode.SelectError))
            {
                return false;
            }

            return IsRefusal((SocketError)(int)socket.GetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Error)!);
        }
        catch (SocketException ex)
        {
            return IsRefusal(ex.SocketErrorCode);
        }
    }

    private static bool IsRefusal(SocketError error) => error == SocketError.ConnectionRefused;
}

/// <summary>Whether the namespace has a default route in a family, from its routing tables (<c>/proc/net/route</c> and <c>/proc/net/ipv6_route</c>). What cannot be read is taken for a route (the family is guarded).</summary>
public static class RoutingTable
{
    public static bool HasDefaultRoute(AddressFamily family) => family == AddressFamily.InterNetworkV6 ? HasDefaultRouteV6("/proc/net/ipv6_route") : HasDefaultRouteV4("/proc/net/route");

    public static bool HasDefaultRouteV4(string file) =>
        Read(file) is not { } lines || lines.Skip(1).Select(line => line.Split('\t', StringSplitOptions.RemoveEmptyEntries)).Any(fields => fields.Length > 7 && fields[1] == "00000000" && fields[7] == "00000000");

    public static bool HasDefaultRouteV6(string file) =>
        Read(file) is not { } lines || lines.Select(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Any(fields => fields.Length > 9 && fields[0] == "00000000000000000000000000000000" && fields[1] == "00" && fields[8] != "ffffffff" && fields[^1] != "lo");

    private static string[]? Read(string file)
    {
        try
        {
            return File.ReadAllLines(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
