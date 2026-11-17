using Geminarr.Core.Browser;
using Geminarr.Host.Http;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Geminarr.Integration.Test;

/// <summary>How the report of the namespace is made: from the listings of the sockets, with Geminarr's own port and the tolerated ports left out, and what cannot be read is exposed.</summary>
public sealed class ProcBrowserExposureFixture : IDisposable
{
    private const string Header = "  sl  local_address rem_address   st tx_queue rx_queue tr tm->when retrnsmt   uid  timeout inode";
    private const string On3000AllInterfaces = "   0: 00000000:0BB8 00000000:0000 0A 00000000:00000000 00:00000000 00000000  1000        0 1 1 0 100 0 0 10 0";
    private const string On3000Loopback = "   0: 0100007F:0BB8 00000000:0000 0A 00000000:00000000 00:00000000 00000000  1000        0 1 1 0 100 0 0 10 0";
    private const string On8282AllInterfaces = "   1: 00000000:205A 00000000:0000 0A 00000000:00000000 00:00000000 00000000  1654        0 2 1 0 100 0 0 10 0";
    private const string On15001AllInterfaces = "   2: 00000000:3A99 00000000:0000 0A 00000000:00000000 00:00000000 00000000  1337        0 3 1 0 100 0 0 10 0";
    private const string On9222V6AllInterfaces = "   0: 00000000000000000000000000000000:2406 00000000000000000000000000000000:0000 0A 00000000:00000000 00:00000000 00000000  1000        0 4 1 0 100 0 0 10 0";

    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"geminarr-proc-{Guid.NewGuid():N}");
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 15, 12, 0, 0, TimeSpan.Zero));

    public ProcBrowserExposureFixture()
    {
        Directory.CreateDirectory(_folder);
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private string V4 => Path.Combine(_folder, "tcp");

    private string V6 => Path.Combine(_folder, "tcp6");

    private sealed class FixedProbe(bool refused) : IEgressProbe
    {
        public bool IsRefused() => refused;
    }

    private ProcBrowserExposure Subject(BrowserOptions? options = null, params string[] addresses) => Subject(options, refused: true, addresses);

    private ProcBrowserExposure Subject(BrowserOptions? options, bool refused, params string[] addresses) =>
        new(Options.Create(options ?? new BrowserOptions { DesktopUrl = "http://127.0.0.1:3000", CdpUrl = "http://127.0.0.1:9222" }), new FakeServer(addresses), _clock, [V4, V6], new FixedProbe(refused));

    private void Write(string file, params string[] sockets) => File.WriteAllLines(file, [Header, .. sockets]);

    [Fact]
    public void should_say_nothing_when_no_browser_is_configured()
    {
        Write(V4, On3000AllInterfaces);

        Subject(new BrowserOptions()).Check().Should().Be(ExposureReport.None);
    }

    [Fact]
    public void should_name_the_ports_that_listen_off_the_loopback_in_both_listings()
    {
        Write(V4, On3000Loopback, On3000AllInterfaces);
        Write(V6, On9222V6AllInterfaces);

        var report = Subject().Check();

        report.Ports.Should().Equal(3000, 9222);
        (report.IsExposed, report.Unreadable).Should().Be((true, false));
    }

    [Fact]
    public void should_leave_out_the_port_of_geminarr_whatever_the_form_of_its_address()
    {
        Write(V4, On8282AllInterfaces);

        foreach (var address in new[] { "http://+:8282", "http://0.0.0.0:8282", "http://[::]:8282", "http://localhost:8282/" })
        {
            Subject(null, address).Check().IsExposed.Should().BeFalse(address);
        }

        Subject().Check().Ports.Should().Equal(new[] { 8282 }, "without the address of Geminarr, its own port is a stranger's");
    }

    [Fact]
    public void should_leave_out_the_ports_that_the_operator_says_another_container_opens_on_purpose()
    {
        Write(V4, On15001AllInterfaces, On3000AllInterfaces);

        Subject(new BrowserOptions { DesktopUrl = "http://127.0.0.1:3000", AllowedOpenPorts = "15001" }).Check().Ports.Should().Equal(3000);
        Subject(new BrowserOptions { DesktopUrl = "http://127.0.0.1:3000", AllowedOpenPorts = "15001;3000" }).Check().IsExposed.Should().BeFalse();
    }

    [Fact]
    public void should_not_take_for_isolated_a_listing_that_cannot_be_read()
    {
        var report = Subject().Check();

        (report.Unreadable, report.IsExposed).Should().Be((true, true), "the IPv4 listing is missing");
        report.Describe().Should().Contain("could not be read");
    }

    [Fact]
    public void should_do_without_the_listing_of_ipv6_where_there_is_none()
    {
        Write(V4, On3000Loopback);

        var report = Subject().Check();

        (report.IsExposed, report.Unreadable).Should().Be((false, false), "no IPv6 here");
    }

    [Fact]
    public void should_keep_the_answer_for_a_few_seconds_and_read_again_after()
    {
        Write(V4, On3000Loopback);
        var subject = Subject();
        subject.Check().IsExposed.Should().BeFalse();
        Write(V4, On3000AllInterfaces);

        subject.Check().IsExposed.Should().BeFalse("kept");
        _clock.Advance(TimeSpan.FromSeconds(16));

        subject.Check().Ports.Should().Equal(3000);
    }

    [Fact]
    public void should_close_the_desktop_while_the_egress_guard_is_not_in_place_and_say_why()
    {
        Write(V4, On8282AllInterfaces);

        var report = Subject(options: null, refused: false, "http://+:8282").Check();

        report.Unguarded.Should().BeTrue();
        report.Reason.Should().Be("egress-unguarded");
        report.IsExposed.Should().BeTrue("a guard that is missing fails open, and nothing else would say so");
        report.Describe().Should().Contain("egress guard").And.Contain("closed");
    }

    [Fact]
    public void should_not_ask_for_the_egress_guard_when_the_setting_says_it_is_held_by_something_else()
    {
        Write(V4, On8282AllInterfaces);

        var report = Subject(new BrowserOptions { CdpUrl = "http://127.0.0.1:9222", RequireEgressGuard = false }, refused: false, "http://+:8282").Check();

        report.IsExposed.Should().BeFalse();
    }

    [Fact]
    public void should_ask_the_guard_once_in_the_time_that_the_answer_is_kept()
    {
        Write(V4, On8282AllInterfaces);
        var probe = new CountingProbe();
        var subject = new ProcBrowserExposure(Options.Create(new BrowserOptions { CdpUrl = "http://127.0.0.1:9222" }), new FakeServer(["http://+:8282"]), _clock, [V4, V6], probe);

        subject.Check();
        subject.Check();
        _clock.Advance(TimeSpan.FromSeconds(16));
        subject.Check();

        probe.Asked.Should().Be(2);
    }

    private sealed class CountingProbe : IEgressProbe
    {
        public int Asked { get; private set; }

        public bool IsRefused()
        {
            Asked++;
            return true;
        }
    }

    private sealed class FakeServer : IServer
    {
        public FakeServer(string[] addresses)
        {
            var feature = new ServerAddressesFeature();
            foreach (var address in addresses)
            {
                feature.Addresses.Add(address);
            }

            Features.Set<IServerAddressesFeature>(feature);
        }

        public IFeatureCollection Features { get; } = new FeatureCollection();

        public void Dispose()
        {
        }

        public Task StartAsync<TContext>(IHttpApplication<TContext> application, CancellationToken cancellationToken)
            where TContext : notnull => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
