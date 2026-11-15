using System.Globalization;

namespace Geminarr.Core.Browser;

/// <summary>
/// What listens in the network namespace that Geminarr shares with the browser container (ADR 0017): the web desktop and the debugging port have no sign-in, so nothing may listen off the loopback
/// but Geminarr's own port. Read from <c>/proc/net/tcp</c> and <c>/proc/net/tcp6</c>, which show every socket of the namespace, whatever the container that opened it.
/// </summary>
public interface IBrowserExposure
{
    /// <summary>The ports that listen off the loopback and are not Geminarr's own or tolerated (a sandbox or a test only); empty when the browser is isolated or none is configured.</summary>
    IReadOnlyList<int> ExposedPorts();
}

/// <summary>The pure part of the audit: which of the listening sockets of a <c>/proc/net/tcp</c> listing are off the loopback.</summary>
public static class ListenerAudit
{
    private const string Listening = "0A";

    /// <summary>The ports of the listening sockets of a listing (a file of <c>/proc/net/tcp</c> or <c>tcp6</c>) whose address is not a loopback address, without the ones in <paramref name="allowed"/>.</summary>
    public static IEnumerable<int> OffTheLoopback(IEnumerable<string> lines, IReadOnlySet<int> allowed) =>
        lines
            .Select(Parse)
            .OfType<(string Address, int Port)>()
            .Where(listener => !IsLoopback(listener.Address) && !allowed.Contains(listener.Port))
            .Select(listener => listener.Port)
            .Distinct()
            .Order();

    /// <summary>A line is <c>sl local_address rem_address st …</c>: the local address is <c>HEX:PORT</c> and the state <c>0A</c> when it listens. The header and any line that is not that give nothing.</summary>
    private static (string Address, int Port)? Parse(string line)
    {
        var fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length < 4 || fields[3] != Listening || fields[1].Split(':') is not [var address, var port] || !int.TryParse(port, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var number))
        {
            return null;
        }

        return (address, number);
    }

    /// <summary>
    /// 127.0.0.0/8 is written byte by byte, the least significant first (so the last two digits are <c>7F</c>); <c>::1</c> is four 32-bit words, each the same way round, and an IPv4 address mapped in an IPv6 one has its
    /// last word like an IPv4 address.
    /// </summary>
    private static bool IsLoopback(string address) => address.Length switch
    {
        8 => address.EndsWith("7F", StringComparison.OrdinalIgnoreCase),
        32 => address.Equals("00000000000000000000000001000000", StringComparison.OrdinalIgnoreCase)
            || (address.StartsWith("0000000000000000FFFF0000", StringComparison.OrdinalIgnoreCase) && address.EndsWith("7F", StringComparison.OrdinalIgnoreCase)),
        _ => false,
    };
}
