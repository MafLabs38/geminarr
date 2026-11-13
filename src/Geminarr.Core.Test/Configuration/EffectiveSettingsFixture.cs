using Geminarr.Core.Access;
using Geminarr.Core.Configuration;
using Geminarr.Core.Test.Access;

namespace Geminarr.Core.Test.Configuration;

public sealed class EffectiveSettingsFixture
{
    private static EffectiveSettings Subject(AuthOptions auth, string? name = "operator-name") =>
        new(Options.Of(new GeminarrOptions { DataFolder = "/config", PublicUrl = "https://geminarr.homelab.example" }), Options.Of(auth), new FakeCredentials(name));

    [Fact]
    public void should_never_show_a_secret_nor_the_path_of_its_file_nor_the_name_of_the_operator()
    {
        var settings = Subject(new AuthOptions { AdminUser = "operator-name", AdminPasswordFile = "/run/secrets/very-private-path" }).Read();

        settings.Should().OnlyContain(setting => !setting.Value.Contains("very-private-path") && !setting.Value.Contains("operator-name"));
        settings.Where(setting => setting.Secret).Should().OnlyContain(setting => setting.Value == "set");
    }

    [Fact]
    public void should_say_not_set_for_a_secret_that_is_not()
    {
        var settings = Subject(new AuthOptions(), name: null).Read();

        settings.Where(setting => setting.Secret).Should().NotBeEmpty().And.OnlyContain(setting => setting.Value == "not set");
    }

    [Fact]
    public void should_name_each_setting_by_its_variable_and_show_the_others_as_they_are()
    {
        var settings = Subject(new AuthOptions { CookieSecurity = CookieSecurityMode.SameAsRequest, SessionHours = 3 }).Read().ToDictionary(setting => setting.Name, setting => setting.Value);

        settings.Should().Contain("GEMINARR__DATAFOLDER", "/config")
            .And.Contain("GEMINARR__PUBLICURL", "https://geminarr.homelab.example")
            .And.Contain("GEMINARR__AUTH__COOKIESECURITY", "SameAsRequest")
            .And.Contain("GEMINARR__AUTH__SESSIONHOURS", "3");
    }
}
