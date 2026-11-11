using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Geminarr.Integration.Test;

/// <summary>Reads its settings from the environment only, as the container does: nothing is injected in memory.</summary>
public sealed class EnvironmentFactory : WebApplicationFactory<Program>;

[Collection("Environment variables")]
public sealed class EnvironmentConfigurationFixture : IDisposable
{
    private const string Variable = "GEMINARR__DATAFOLDER";

    private readonly string _folder = Directory.CreateTempSubdirectory("geminarr-env-").FullName;

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(Variable, null);
        Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public async Task should_read_the_data_folder_from_the_prefixed_environment_variable()
    {
        Environment.SetEnvironmentVariable(Variable, _folder);
        using var factory = new EnvironmentFactory();

        var response = await factory.CreateClient().GetAsync("/health/ready", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
