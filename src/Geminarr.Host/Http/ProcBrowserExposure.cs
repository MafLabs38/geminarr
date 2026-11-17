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
    private readonly IEgressProbe _egress;
    private readonly Lock _gate = new();
    private DateTimeOffset _readAt = DateTimeOffset.MinValue;
    private ExposureReport _report = ExposureReport.None;
    private bool _refreshing;

    public ProcBrowserExposure(IOptions<BrowserOptions> options, IServer server, TimeProvider clock, IEgressProbe egress)
        : this(options, server, clock, ProcFiles, egress)
    {
    }

    /// <summary>For a test: the listings are files of its own (the IPv4 one, then the IPv6 one).</summary>
    public ProcBrowserExposure(IOptions<BrowserOptions> options, IServer server, TimeProvider clock, string[] files, IEgressProbe egress)
    {
        _egress = egress;
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

        bool refresh;
        ExposureReport current;
        lock (_gate)
        {
            // The answer is kept for a few seconds. One caller reads it again (the probe of the guard may wait up to a second when the guard is missing) and the others keep the last answer meanwhile, so that
            // no request waits for a refresh that another one is making.
            var now = _clock.GetUtcNow();
            // The very first answer is made by everybody who asks: nothing is known before it, and what is not known is not taken for good.
            refresh = now - _readAt >= KeepFor && (!_refreshing || _readAt == DateTimeOffset.MinValue);
            _refreshing |= refresh;
            current = _report;
        }

        if (!refresh)
        {
            return current;
        }

        ExposureReport report;
        try
        {
            report = Read(browser);
        }
        finally
        {
            lock (_gate)
            {
                _refreshing = false;
            }
        }

        lock (_gate)
        {
            _report = report;
            _readAt = _clock.GetUtcNow();
        }

        return report;
    }

    private ExposureReport Read(BrowserOptions browser)
    {
        var allowed = new HashSet<int>(OwnPorts());
        allowed.UnionWith(OriginPolicy.Entries(browser.AllowedOpenPorts).Select(port => int.Parse(port, System.Globalization.CultureInfo.InvariantCulture)));
        // Geminarr's own egress is the guard's (ADR 0020): a guard that is not there is said, and the desktop and the reading are closed like for a port that listens.
        var unguarded = browser.RequireEgressGuard && !_egress.IsRefused();
        // The listing of IPv4 sockets always exists on Linux; the one of IPv6 only where IPv6 does. What cannot be read is not taken for isolated.
        if (ReadLines(_files[0]) is not { } v4)
        {
            return new ExposureReport([], Unreadable: true, unguarded);
        }

        var v6Exists = File.Exists(_files[1]);
        var v6 = v6Exists ? ReadLines(_files[1]) : [];
        return new ExposureReport([.. ListenerAudit.OffTheLoopback(v4.Concat(v6 ?? []), allowed)], Unreadable: v6Exists && v6 is null, unguarded);
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
