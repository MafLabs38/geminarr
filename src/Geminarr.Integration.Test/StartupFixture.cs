using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Geminarr.Integration.Test;

/// <summary>A host whose data folder setting is blank, as a manifest with an empty value would give it.</summary>
public sealed class BlankDataFolderFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["DataFolder"] = " " }));
}

public sealed class StartupFixture
{
    [Fact]
    public void should_refuse_to_start_and_name_the_setting_when_the_data_folder_is_blank()
    {
        using var factory = new BlankDataFolderFactory();

        var start = () => factory.CreateClient();

        start.Should().Throw<Exception>().Which.ToString().Should().Contain("GEMINARR__DATAFOLDER");
    }
}
