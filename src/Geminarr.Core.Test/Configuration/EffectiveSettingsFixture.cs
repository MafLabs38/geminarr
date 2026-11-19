using Geminarr.Core.Access;
using Geminarr.Core.Browser;
using Geminarr.Core.Configuration;
using Geminarr.Core.Extension;
using Geminarr.Core.Identity;
using Geminarr.Core.Link;
using Geminarr.Core.Pipeline;
using Geminarr.Core.Test.Access;

namespace Geminarr.Core.Test.Configuration;

public sealed class EffectiveSettingsFixture
{
    private static EffectiveSettings Subject(AuthOptions auth, string? name = "operator-name", IdentityOptions? identity = null, BrowserOptions? browser = null, ExtensionOptions? extension = null) =>
        new(Options.Of(new GeminarrOptions { DataFolder = "/config", PublicUrl = "https://geminarr.homelab.example" }), Options.Of(auth), new FakeCredentials(name), Options.Of(identity ?? new IdentityOptions()), Options.Of(new MnemarrOptions()), [new ReaderSettingsSection(Options.Of(new ReaderOptions())), new BrowserSettingsSection(Options.Of(browser ?? new BrowserOptions())), new ExtensionSettingsSection(Options.Of(extension ?? new ExtensionOptions())), new RetentionSettingsSection(Options.Of(new RetentionOptions()))]);

    [Fact]
    public void should_never_show_a_secret_nor_the_path_of_its_file_nor_the_name_of_the_operator()
    {
        var settings = Subject(new AuthOptions { AdminUser = "operator-name", AdminPasswordFile = "/run/secrets/very-private-path" }, extension: new ExtensionOptions { TokenFile = "/run/secrets/very-private-path" }).Read();

        settings.Should().OnlyContain(setting => !setting.Value.Contains("very-private-path") && !setting.Value.Contains("operator-name"));
        settings.Where(setting => setting.Secret).Should().OnlyContain(setting => setting.Value == "set");
    }

    [Fact]
    public void should_show_the_identity_but_not_the_path_of_the_logo()
    {
        var logoOptions = new IdentityOptions { Name = "Lab", LogoFile = "/run/identity/very-private-logo.png", Colors = { Name = "#3ee0c5" } };

        var settings = Subject(new AuthOptions(), identity: logoOptions).Read().ToDictionary(setting => setting.Name, setting => setting.Value);

        settings.Should().Contain("GEMINARR__IDENTITY__NAME", "Lab")
            .And.Contain("GEMINARR__IDENTITY__LOGOFILE", "set")
            .And.Contain("GEMINARR__IDENTITY__COLORS__NAME", "#3ee0c5")
            .And.Contain("GEMINARR__IDENTITY__COLORS__VERSION", "not set");
        settings.Values.Should().NotContain(value => value.Contains("very-private-logo"));
    }

    [Fact]
    public void should_show_the_browser_settings()
    {
        var settings = Subject(new AuthOptions()).Read().ToDictionary(setting => setting.Name, setting => setting.Value);

        settings.Should().ContainKey("GEMINARR__BROWSER__DESKTOPURL").And.ContainKey("GEMINARR__BROWSER__DESKTOPPORT").And.ContainKey("GEMINARR__BROWSER__REQUIREEGRESSGUARD");
        settings.Should().NotContainKey("GEMINARR__BROWSER__CDPURL");
        settings["GEMINARR__BROWSER__DESKTOPURL"].Should().Be("not set");
    }

    [Fact]
    public void should_never_show_the_address_of_the_desktop()
    {
        var browser = new BrowserOptions { DesktopUrl = "http://127.0.0.1:3000" };

        var settings = Subject(new AuthOptions(), browser: browser).Read().ToDictionary(setting => setting.Name, setting => setting.Value);

        settings["GEMINARR__BROWSER__DESKTOPURL"].Should().Be("set");
        settings.Values.Should().NotContain(value => value.Contains("3000", StringComparison.Ordinal));
    }

    [Fact]
    public void should_show_where_the_desktop_has_its_own_origin()
    {
        var plain = Subject(new AuthOptions(), browser: new BrowserOptions()).Read().ToDictionary(setting => setting.Name, setting => setting.Value);
        var proxied = Subject(new AuthOptions(), browser: new BrowserOptions { DesktopPort = 9000, DesktopPublicUrl = "https://desktop.homelab.example" }).Read().ToDictionary(setting => setting.Name, setting => setting.Value);

        plain["GEMINARR__BROWSER__DESKTOPPORT"].Should().Be("8283");
        plain["GEMINARR__BROWSER__DESKTOPPUBLICURL"].Should().Be("not set");
        proxied["GEMINARR__BROWSER__DESKTOPPORT"].Should().Be("9000");
        proxied["GEMINARR__BROWSER__DESKTOPPUBLICURL"].Should().Be("https://desktop.homelab.example");
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

    [Fact]
    public void should_say_only_whether_the_token_of_the_extension_is_set_and_never_where_its_file_is()
    {
        var settings = Subject(new AuthOptions(), extension: new ExtensionOptions { TokenFile = "/run/secrets/very-private-token" }).Read();

        settings.Single(setting => setting.Name == "GEMINARR__EXTENSION__TOKENFILE").Should().Be(new SettingValue("GEMINARR__EXTENSION__TOKENFILE", "set", true));
        settings.Should().NotContain(setting => setting.Value.Contains("very-private-token"));
        settings.Select(setting => setting.Name).Should().Contain(["GEMINARR__EXTENSION__PACKAGEFOLDER", "GEMINARR__EXTENSION__PUBLICURL", "GEMINARR__EXTENSION__SILENTAFTERMINUTES", "GEMINARR__RETENTION__RUNSDAYS"]);
    }
}
