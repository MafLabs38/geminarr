using System.Net.Http.Json;
using Geminarr.Api.V1.Health;
using Geminarr.Api.V1.Settings;

namespace Geminarr.Integration.Test;

[Collection("Host")]
public sealed class HealthAndSettingsApiFixture
{
    private readonly GeminarrFactory _factory;

    public HealthAndSettingsApiFixture(GeminarrFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task should_list_every_check_with_its_state_and_nothing_about_the_host()
    {
        using var client = await _factory.SignedInClientAsync();

        var health = await client.GetFromJsonAsync<HealthResource>("/api/v1/health", TestContext.Current.CancellationToken);

        // A fresh container is ready and healthy where it must be; it is not linked to a Mnemarr and not connected to an assistant, and says so with a code.
        health!.Status.Should().Be("Degraded");
        health.Checks.Select(check => check.Name).Should().BeEquivalentTo("data-folder", "database", "operator", "connection", "profile", "mnemarr", "outbox");
        health.Checks.Where(check => check.Status == "Degraded").ToDictionary(check => check.Name, check => check.Reason).Should().Equal(new Dictionary<string, string?> { ["connection"] = "not-connected", ["mnemarr"] = "mnemarr-not-linked" });
        health.Checks.Where(check => check.Name is "data-folder" or "database" or "operator" or "outbox").Should().OnlyContain(check => check.Status == "Healthy" && check.Reason == null);
        var body = await client.GetStringAsync("/api/v1/health", TestContext.Current.CancellationToken);
        body.Should().NotContain(_factory.DataFolder);
    }

    [Fact]
    public async Task should_show_the_settings_with_the_secrets_hidden()
    {
        using var client = await _factory.SignedInClientAsync();

        var settings = await client.GetFromJsonAsync<SettingsResource>("/api/v1/settings", TestContext.Current.CancellationToken);

        settings!.Settings.Should().Contain(setting => setting.Name == "GEMINARR__DATAFOLDER" && setting.Value == _factory.DataFolder);
        var body = await client.GetStringAsync("/api/v1/settings", TestContext.Current.CancellationToken);
        body.Should().NotContain(GeminarrFactoryBase.OperatorPassword).And.NotContain(GeminarrFactoryBase.OperatorName).And.NotContain("geminarr-password-");
    }
}
