using Geminarr.Core.Browser;
using Geminarr.Core.Health;
using Geminarr.Core.Pipeline;
using Geminarr.Core.Test.Access;
using Geminarr.Core.Test.Support;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Geminarr.Core.Test.Health;

public sealed class ProfileHealthFixture
{
    private static readonly HealthCheckContext Context = new();

    private static Task<HealthCheckResult> Check(ProfileState state, ReaderKind kind = ReaderKind.Web) =>
        new ProfileHealthCheck(new FakeBrowser { State = state }, Options.Of(new ReaderOptions { Kind = kind })).CheckHealthAsync(Context, TestContext.Current.CancellationToken);

    [Fact]
    public async Task should_not_matter_when_no_browser_reads()
    {
        (await Check(new ProfileState(Exists: false, HasSession: false, Private: true), ReaderKind.None)).Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public async Task should_be_healthy_when_the_folder_is_there_readable_and_private()
    {
        (await Check(new ProfileState(Exists: true, HasSession: false, Private: true))).Status.Should().Be(HealthStatus.Healthy);
    }

    [Theory]
    [InlineData(false, true, true, HealthReasons.ProfileMissing)]
    [InlineData(true, false, true, HealthReasons.ProfileUnreadable)]
    [InlineData(true, true, false, HealthReasons.ProfileNotPrivate)]
    public async Task should_say_what_is_wrong_with_the_folder_of_the_profile(bool exists, bool readable, bool isPrivate, string reason)
    {
        var result = await Check(new ProfileState(exists, HasSession: false, isPrivate, readable));

        result.Status.Should().Be(HealthStatus.Degraded);
        result.Data[HealthReasons.Key].Should().Be(reason);
    }
}
