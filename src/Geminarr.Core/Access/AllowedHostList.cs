using Geminarr.Core.Browser;
using Geminarr.Core.Configuration;
using Microsoft.Extensions.Options;

namespace Geminarr.Core.Access;

/// <summary>The names the page may be reached by (<see cref="GeminarrOptions.AllowedHostNames"/> and the host of <see cref="GeminarrOptions.PublicUrl"/>).</summary>
public sealed class AllowedHostList
{
    private readonly HashSet<string> _hosts;
    private readonly bool _any;
    private readonly string? _desktopHost;

    public AllowedHostList(IOptions<GeminarrOptions> options, IOptions<BrowserOptions>? browser = null)
    {
        _desktopHost = HostOfPublicUrl(browser?.Value.DesktopPublicUrl);
        var configured = Split(options.Value.AllowedHostNames);
        _any = configured.Contains("*");
        _hosts = new HashSet<string>(configured, StringComparer.OrdinalIgnoreCase);
        if (HostOfPublicUrl(options.Value.PublicUrl) is { } publicHost)
        {
            _hosts.Add(publicHost);
        }
    }

    /// <summary>Whether every name is allowed (<c>*</c>): the list then says nothing about who a page is.</summary>
    public bool AllowsAny => _any;

    /// <summary>Whether a <c>Host</c> header names an allowed host. A missing or empty one is not.</summary>
    public bool IsAllowed(string? hostHeader)
    {
        if (_any)
        {
            return true;
        }

        var host = HostOf(hostHeader);
        return host is not null && _hosts.Contains(host);
    }

    /// <summary>Whether a <c>Host</c> header names a host that the desktop's own origin answers to: the page's names, and the host that a proxy gives the desktop (<see cref="BrowserOptions.DesktopPublicUrl"/>), which the page's origin does not answer to.</summary>
    public bool IsAllowedOnDesktopOrigin(string? hostHeader) =>
        IsAllowed(hostHeader) || (_desktopHost is not null && string.Equals(HostOf(hostHeader), _desktopHost, StringComparison.OrdinalIgnoreCase));

    /// <summary>The names of a setting: separated by commas, semicolons or spaces, each as a host with no port.</summary>
    public static IReadOnlyList<string> Split(string? setting) =>
        [.. (setting ?? string.Empty).Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(entry => entry == "*" ? entry : HostOf(entry)).OfType<string>()];

    /// <summary>The host of an address that may be given as the public one; null when it is not an absolute http or https address with a host and no credentials.</summary>
    public static string? HostOfPublicUrl(string? address) =>
        Uri.TryCreate(address, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" && uri.Host.Length > 0 && uri.UserInfo.Length == 0
            ? HostOf(uri.Authority)
            : null;

    /// <summary>The host of a <c>Host</c> header or an authority, without its port: <c>localhost:8282</c> is <c>localhost</c>, <c>[::1]:8282</c> is <c>[::1]</c>.</summary>
    public static string? HostOf(string? hostHeader)
    {
        var value = hostHeader?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        if (value.StartsWith('['))
        {
            var end = value.IndexOf(']');
            return end < 0 ? null : value[..(end + 1)].ToLowerInvariant();
        }

        var colon = value.IndexOf(':');
        var host = colon < 0 ? value : value[..colon];
        return host.Length == 0 ? null : host.ToLowerInvariant();
    }
}
