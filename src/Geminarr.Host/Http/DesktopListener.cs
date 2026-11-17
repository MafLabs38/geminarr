using Geminarr.Core.Browser;
using Microsoft.AspNetCore.Http;

namespace Geminarr.Host.Http;

/// <summary>Where the desktop's own origin listens (ADR 0019): the interface's address with the port of the desktop, so that the desktop is open on the same interfaces and the same scheme as the page, and not wider.</summary>
public static class DesktopListener
{
    /// <summary>
    /// The addresses to listen on: those that are configured and, when a desktop is configured, the desktop's port on the interface of the first. Nothing is added without a desktop. A desktop with no address to build on, a
    /// port that is the interface's own, or a port that is not a number is refused, with the setting named: a desktop that is configured and never opens would only be seen as a window that does not load.
    /// </summary>
    public static string? Plan(string? urls, string? desktopUrl, string? portSetting)
    {
        if (string.IsNullOrWhiteSpace(desktopUrl))
        {
            return urls;
        }

        var port = Port(portSetting);
        var entries = (urls ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (entries.Length == 0)
        {
            throw new InvalidOperationException("A desktop is configured (GEMINARR__BROWSER__DESKTOPURL) but there is no address to listen on (GEMINARR__URLS): the desktop's port is added to the interface's own address.");
        }

        var addresses = entries.Select(BindingAddress.Parse).ToList();
        if (addresses[0].Port == port)
        {
            throw new InvalidOperationException("Browser:DesktopPort (GEMINARR__BROWSER__DESKTOPPORT) is the port of the interface: the desktop needs a port of its own.");
        }

        if (addresses.Any(address => address.Port == port))
        {
            return urls;
        }

        var first = addresses[0];
        return string.Join(';', [.. entries, $"{first.Scheme}://{first.Host}:{port}"]);
    }

    private static int Port(string? setting)
    {
        if (string.IsNullOrWhiteSpace(setting))
        {
            return BrowserOptions.DefaultDesktopPort;
        }

        return int.TryParse(setting, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var port) && port is >= 1 and <= 65_535
            ? port
            : throw new InvalidOperationException("Browser:DesktopPort (GEMINARR__BROWSER__DESKTOPPORT) must be a port number.");
    }
}
