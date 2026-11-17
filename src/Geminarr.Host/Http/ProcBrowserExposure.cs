using System.Text.RegularExpressions;
using Geminarr.Core.Browser;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Options;

namespace Geminarr.Host.Http;

/// <summary>
/// Reads what listens in the network namespace that Geminarr shares with the browser container, from <c>/proc/net/tcp</c> and <c>tcp6</c> (ADR 0017): the web desktop and the debugging port have no sign-in,
/// so any socket that listens off the loopback, other than Geminarr's own port, is a way to the browser that bypasses Geminarr. The answer is kept for a few seconds. A host without those files (not Linux)
/// has nothing to say, and nothing is checked when no browser is configured.
/// </summary>
public sealed partial class ProcBrowserExposure : IBrowserExposure
{
    private static readonly TimeSpan KeepFor = TimeSpan.FromSeconds(15);
    private static readonly string[] ProcFiles = ["/proc/net/tcp", "/proc/net/tcp6"];

    private readonly string[] _files;
    private readonly IOptions<BrowserOptions> _options;
    private readonly IServer _server;
    private readonly TimeProvider _clock;
    private readonly Lock _gate = new();
    private DateTimeOffset _readAt = DateTimeOffset.MinValue;
    private ExposureReport _report = ExposureReport.None;

    public ProcBrowserExposure(IOptions<BrowserOptions> options, IServer server, TimeProvider clock)
        : this(options, server, clock, ProcFiles)
    {
    }

    /// <summary>For a test: the listings are files of its own (the IPv4 one, then the IPv6 one).</summary>
    public ProcBrowserExposure(IOptions<BrowserOptions> options, IServer server, TimeProvider clock, string[] files)
    {
        _files = files;
        _options = options;
        _server = server;
        _clock = clock;
    }

    public ExposureReport Check()
    {
        var browser = _options.Value;
        if (string.IsNullOrWhiteSpace(browser.CdpUrl) && string.IsNullOrWhiteSpace(browser.DesktopUrl))
        {
            return ExposureReport.None;
        }

        lock (_gate)
        {
            var now = _clock.GetUtcNow();
            if (now - _readAt >= KeepFor)
            {
                _report = Read(browser);
                _readAt = now;
            }

            return _report;
        }
    }

    private ExposureReport Read(BrowserOptions browser)
    {
        var allowed = new HashSet<int>(OwnPorts());
        allowed.UnionWith(OriginPolicy.Entries(browser.AllowedOpenPorts).Select(port => int.Parse(port, System.Globalization.CultureInfo.InvariantCulture)));
        // The listing of IPv4 sockets always exists on Linux; the one of IPv6 only where IPv6 does. What cannot be read is not taken for isolated.
        if (ReadLines(_files[0]) is not { } v4)
        {
            return new ExposureReport([], Unreadable: true);
        }

        var v6Exists = File.Exists(_files[1]);
        var v6 = v6Exists ? ReadLines(_files[1]) : [];
        return new ExposureReport([.. ListenerAudit.OffTheLoopback(v4.Concat(v6 ?? []), allowed)], Unreadable: v6Exists && v6 is null);
    }

    /// <summary>The ports that Geminarr itself listens on (<c>http://+:8282</c>, <c>http://0.0.0.0:8282</c>…): the one place that something may listen off the loopback.</summary>
    private IEnumerable<int> OwnPorts() =>
        (_server.Features.Get<IServerAddressesFeature>()?.Addresses ?? [])
            .Select(address => PortOf().Match(address))
            .Where(match => match.Success)
            .Select(match => int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture));

    private static string[]? ReadLines(string path)
    {
        try
        {
            return File.ReadAllLines(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    [GeneratedRegex(@":(\d+)/?$", RegexOptions.None, matchTimeoutMilliseconds: 200)]
    private static partial Regex PortOf();
}
