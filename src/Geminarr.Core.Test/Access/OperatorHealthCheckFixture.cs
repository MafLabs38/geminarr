using Geminarr.Core.Access;
using Geminarr.Core.Health;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Geminarr.Core.Test.Access;

public sealed class OperatorHealthCheckFixture
{
    [Fact]
    public async Task should_be_healthy_when_somebody_can_sign_in()
    {
        var result = await Check(OperatorStatus.Ready);

        result.Status.Should().Be(HealthStatus.Healthy);
    }

    [Theory]
    [InlineData(OperatorStatus.NotConfigured, HealthReasons.OperatorNotConfigured)]
    [InlineData(OperatorStatus.PasswordUnreadable, HealthReasons.OperatorPasswordUnreadable)]
    [InlineData(OperatorStatus.PasswordWeak, HealthReasons.OperatorPasswordWeak)]
    public async Task should_be_degraded_and_never_unhealthy_with_a_stable_reason_when_nobody_can_sign_in(OperatorStatus status, string reason)
    {
        var result = await Check(status);

        result.Status.Should().Be(HealthStatus.Degraded);
        result.Data[HealthReasons.Key].Should().Be(reason);
        result.Description.Should().NotContain("/").And.NotContain("operator-name");
    }

    private static Task<HealthCheckResult> Check(OperatorStatus status) =>
        new OperatorHealthCheck(new FakeCredentials(status: status)).CheckHealthAsync(new HealthCheckContext(), TestContext.Current.CancellationToken);
}
