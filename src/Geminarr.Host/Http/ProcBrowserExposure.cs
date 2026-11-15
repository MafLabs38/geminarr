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
    private static readonly string[] Files = ["/proc/net/tcp", "/proc/net/tcp6"];

    private readonly IOptions<BrowserOptions> _options;
    private readonly IServer _server;
    private readonly TimeProvider _clock;
    private readonly Lock _gate = new();
    private DateTimeOffset _readAt = DateTimeOffset.MinValue;
    private IReadOnlyList<int> _exposed = [];

    public ProcBrowserExposure(IOptions<BrowserOptions> options, IServer server, TimeProvider clock)
    {
        _options = options;
        _server = server;
        _clock = clock;
    }

    public IReadOnlyList<int> ExposedPorts()
    {
        var browser = _options.Value;
        if (string.IsNullOrWhiteSpace(browser.CdpUrl) && string.IsNullOrWhiteSpace(browser.DesktopUrl))
        {
            return [];
        }

        lock (_gate)
        {
            var now = _clock.GetUtcNow();
            if (now - _readAt >= KeepFor)
            {
                _exposed = Read(browser);
                _readAt = now;
            }

            return _exposed;
        }
    }

    private IReadOnlyList<int> Read(BrowserOptions browser)
    {
        var allowed = new HashSet<int>(OwnPorts());
        allowed.UnionWith(OriginPolicy.Entries(browser.AllowedOpenPorts).Select(int.Parse));
        var lines = Files.Where(File.Exists).SelectMany(ReadLinesOrNone);
        return [.. ListenerAudit.OffTheLoopback(lines, allowed)];
    }

    /// <summary>The ports that Geminarr itself listens on (<c>http://+:8282</c>, <c>http://0.0.0.0:8282</c>…): the one place that something may listen off the loopback.</summary>
    private IEnumerable<int> OwnPorts() =>
        (_server.Features.Get<IServerAddressesFeature>()?.Addresses ?? [])
            .Select(address => PortOf().Match(address))
            .Where(match => match.Success)
            .Select(match => int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture));

    private static IEnumerable<string> ReadLinesOrNone(string path)
    {
        try
        {
            return File.ReadAllLines(path);
        }
        catch (IOException)
        {
            // The listing of the namespace is not readable: nothing is known, and nothing is said.
            return [];
        }
    }

    [GeneratedRegex(@":(\d+)/?$")]
    private static partial Regex PortOf();
}
