using System.Net;
using System.Net.Sockets;
using Geminarr.Core.Browser;

namespace Geminarr.Host.Http;

/// <summary>
/// The probe of the egress guard (ADR 0020), for both families: a connection to an address of the range that is reserved for documentation (<c>192.0.2.1</c> and <c>2001:db8::1</c>, which no network routes) is refused at once by the
/// guard (a reset), and otherwise waits for a route that is not there, finds none, or goes out. Nothing is sent: the connection is opened and dropped. It measures the property that matters (an address on no list is refused to
/// Geminarr's process), not the way that it is done. Where a family has no route at all, its guard has nothing to say: Geminarr could not use it either.
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

    public bool IsRefused() => Outcome(_v4) && Outcome(_v6);

    private bool Outcome(IPEndPoint target)
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

            return IsRefusal((SocketError)(int)socket.GetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Error)!);
        }
        catch (SocketException ex)
        {
            return IsRefusal(ex.SocketErrorCode);
        }
    }

    /// <summary>
    /// A refusal at once, or no route at all in that family (the system says so itself, before anything is sent: Geminarr could not use the family, so there is nothing to guard). What a router says on the way
    /// (host unreachable) is not that: it is an answer from the network, and a guard that is missing would get it too.
    /// </summary>
    private static bool IsRefusal(SocketError error) =>
        error is SocketError.ConnectionRefused or SocketError.NetworkUnreachable or SocketError.AddressNotAvailable or SocketError.AddressFamilyNotSupported or SocketError.ProtocolNotSupported;
}
