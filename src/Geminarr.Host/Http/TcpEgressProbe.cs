using System.Net;
using System.Net.Sockets;
using Geminarr.Core.Browser;

namespace Geminarr.Host.Http;

/// <summary>
/// The probe of the egress guard (ADR 0020), for both families: a connection to an address of the range that is reserved for documentation (<c>192.0.2.1</c> and <c>2001:db8::1</c>, which no network routes) is refused at once by the
/// guard (a reset), and otherwise waits for a route that is not there, finds none, or goes out. Nothing is sent: the connection is opened and dropped. It measures the property that matters (an address on no list is refused to
/// Geminarr's process), not the way that it is done. Where IPv6 has no route at all, the guard of IPv6 has nothing to say: Geminarr could not use it either.
/// </summary>
public sealed class TcpEgressProbe : IEgressProbe
{
    private readonly IPEndPoint _v4;
    private readonly IPEndPoint _v6;
    private readonly int _waitMicroseconds;

    public TcpEgressProbe()
        : this(new IPEndPoint(IPAddress.Parse("192.0.2.1"), 9), new IPEndPoint(IPAddress.Parse("2001:db8::1"), 9), TimeSpan.FromMilliseconds(1_000))
    {
    }

    public TcpEgressProbe(IPEndPoint v4, IPEndPoint v6, TimeSpan wait)
    {
        _v4 = v4;
        _v6 = v6;
        _waitMicroseconds = (int)(wait.TotalMilliseconds * 1_000);
    }

    public bool IsRefused() => Outcome(_v4, noRouteIsRefusal: false) && Outcome(_v6, noRouteIsRefusal: true);

    private bool Outcome(IPEndPoint target, bool noRouteIsRefusal)
    {
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

            return IsRefusal((SocketError)(int)socket.GetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Error)!, noRouteIsRefusal);
        }
        catch (SocketException ex)
        {
            return IsRefusal(ex.SocketErrorCode, noRouteIsRefusal);
        }
    }

    private static bool IsRefusal(SocketError error, bool noRouteIsRefusal) =>
        error == SocketError.ConnectionRefused
        || (noRouteIsRefusal && error is SocketError.NetworkUnreachable or SocketError.HostUnreachable or SocketError.AddressNotAvailable or SocketError.AddressFamilyNotSupported or SocketError.ProtocolNotSupported);
}
