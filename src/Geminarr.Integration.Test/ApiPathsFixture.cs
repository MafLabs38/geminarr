using Geminarr.Host.Http;
using Microsoft.AspNetCore.Http;

namespace Geminarr.Integration.Test;

public sealed class ApiPathsFixture
{
    [Theory]
    [InlineData("/api", true)]
    [InlineData("/api/v1/system/status", true)]
    [InlineData("/API/v1/system/status", true)]
    [InlineData("/openapi/v1.json", true)]
    [InlineData("/apiary", false)]
    [InlineData("/openapi-x", false)]
    [InlineData("/health/live", false)]
    [InlineData("/", false)]
    [InlineData("/system/status", false)]
    public void should_hold_to_the_api_rules_only_what_lives_under_the_api_and_its_description(string path, bool expected)
    {
        ApiPaths.IsApi(new PathString(path)).Should().Be(expected);
    }
}
