using Geminarr.Core.Connection;
using Geminarr.Core.Health;
using Geminarr.Core.Pipeline;
using Geminarr.Core.Test.Access;
using Geminarr.Core.Test.Support;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Geminarr.Core.Test.Health;

/// <summary>The check of the attachment to the browser container: quiet when no browser is asked for, and it names the failure with a stable code when one is.</summary>
public sealed class BrowserHealthCheckFixture
{
    private static readonly HealthCheckContext Context = new();

    private static string? Reason(HealthCheckResult result) => result.Data.TryGetValue(HealthReasons.Key, out var reason) ? reason as string : null;

    private static Task<HealthCheckResult> Check(FakeBrowser browser, ReaderKind kind) =>
        new BrowserHealthCheck(browser, Options.Of(new ReaderOptions { Kind = kind })).CheckHealthAsync(Context, TestContext.Current.CancellationToken);

    [Theory]
    [InlineData(ReaderKind.None)]
    [InlineData(ReaderKind.Demo)]
    public async Task should_say_nothing_when_no_browser_reads(ReaderKind kind)
    {
        var result = await Check(new FakeBrowser { Configured = false }, kind);

        result.Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public async Task should_say_the_browser_is_missing_when_the_reader_is_the_browser_and_none_is_configured()
    {
        var result = await Check(new FakeBrowser { Configured = false }, ReaderKind.Web);

        result.Status.Should().Be(HealthStatus.Degraded);
        Reason(result).Should().Be(ConnectionReasons.NotConnected);
    }

    [Fact]
    public async Task should_say_browser_failed_when_the_last_attachment_did_not_reach_the_browser()
    {
        var result = await Check(new FakeBrowser { Reachable = false }, ReaderKind.Web);

        result.Status.Should().Be(HealthStatus.Degraded);
        Reason(result).Should().Be(ConnectionReasons.BrowserFailed);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(null)]
    public async Task should_be_healthy_when_the_browser_answered_or_was_not_tried_yet(bool? reachable)
    {
        var result = await Check(new FakeBrowser { Reachable = reachable }, ReaderKind.Web);

        result.Status.Should().Be(HealthStatus.Healthy);
    }
}
