using System.Net;
using System.Net.Sockets;
using Geminarr.Core.Browser;

namespace Geminarr.Host.Http;

/// <summary>
/// The probe of the egress guard (ADR 0020): a connection to an address of the range that is reserved for documentation (<c>192.0.2.1</c>, which no network routes) is refused at once by the guard (a reset), and otherwise waits for a
/// route that is not there, finds none, or goes out. Nothing is sent: the connection is opened and dropped.
/// </summary>
public sealed class TcpEgressProbe : IEgressProbe
{
    private readonly IPEndPoint _target;
    private readonly int _waitMicroseconds;

    public TcpEgressProbe()
        : this(new IPEndPoint(IPAddress.Parse("192.0.2.1"), 9), TimeSpan.FromMilliseconds(1_500))
    {
    }

    public TcpEgressProbe(IPEndPoint target, TimeSpan wait)
    {
        _target = target;
        _waitMicroseconds = (int)(wait.TotalMilliseconds * 1_000);
    }

    public bool IsRefused()
    {
        try
        {
            using var socket = new Socket(_target.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { Blocking = false };
            try
            {
                socket.Connect(_target);
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

            return (SocketError)(int)socket.GetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Error)! == SocketError.ConnectionRefused;
        }
        catch (SocketException ex)
        {
            return ex.SocketErrorCode == SocketError.ConnectionRefused;
        }
    }
}
