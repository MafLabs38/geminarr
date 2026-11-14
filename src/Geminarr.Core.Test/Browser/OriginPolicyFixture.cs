using Geminarr.Core.Browser;

namespace Geminarr.Core.Test.Browser;

public sealed class OriginPolicyFixture
{
    private static BrowserOptions Options(bool insecure = false) => new()
    {
        AssistantUrl = "https://assistant.example/app",
        SignInHosts = "accounts.example",
        AllowedHosts = "*.static.example;api.example",
        AllowInsecureAssistant = insecure,
    };

    [Theory]
    [InlineData("https://assistant.example/app", true)]
    [InlineData("https://assistant.example/anything?x=1", true)]
    [InlineData("https://ASSISTANT.example/app", true)]
    [InlineData("https://fonts.static.example/a.woff2", true)]
    [InlineData("https://api.example/v1", true)]
    [InlineData("https://static.example/", false)]
    [InlineData("https://evilstatic.example/", false)]
    [InlineData("https://other.example/", false)]
    [InlineData("https://accounts.example/signin", false)]
    [InlineData("http://assistant.example/app", false)]
    [InlineData("ftp://assistant.example/app", false)]
    [InlineData("not an address", false)]
    [InlineData("data:image/png;base64,AAAA", true)]
    [InlineData("blob:https://assistant.example/1234", true)]
    [InlineData("about:blank", true)]
    public void should_load_the_assistant_and_its_own_hosts_and_nothing_else_when_only_checking(string address, bool allowed)
    {
        OriginPolicy.Allows(address, Options(), liveView: false).Should().Be(allowed);
    }

    [Theory]
    [InlineData("https://assistant.example/app", true, false)]
    [InlineData("https://fonts.static.example/a.woff2", true, false)]
    [InlineData("https://fonts.static.example/a.woff2", false, true)]
    [InlineData("https://api.example/v1", false, true)]
    [InlineData("https://other.example/", false, true)]
    public void should_go_to_a_page_only_if_it_is_the_assistants_and_load_the_files_of_the_others(string address, bool allowed, bool navigation)
    {
        OriginPolicy.Allows(address, Options(), liveView: false, navigation).Should().Be(allowed);
    }

    [Fact]
    public void should_go_to_a_sign_in_page_in_a_live_view_and_never_to_a_host_of_files()
    {
        OriginPolicy.Allows("https://accounts.example/signin", Options(), liveView: true, navigation: true).Should().BeTrue();
        OriginPolicy.Allows("https://api.example/v1", Options(), liveView: true, navigation: true).Should().BeFalse();
    }

    [Theory]
    [InlineData("wss://assistant.example/socket", true)]
    [InlineData("wss://api.example/socket", true)]
    [InlineData("wss://evil.example/socket", false)]
    [InlineData("ws://assistant.example/socket", false)]
    [InlineData("WSS://ASSISTANT.example/socket", true)]
    public void should_open_a_web_socket_only_to_the_hosts_that_may_be_loaded(string address, bool allowed)
    {
        OriginPolicy.AllowsWebSocket(address, Options(), liveView: false).Should().Be(allowed);
    }

    [Theory]
    [InlineData("https://storage.googleapis.com/a-bucket/page.html")]
    [InlineData("https://someone.googleusercontent.com/page")]
    [InlineData("https://docs.google.com/document/d/1")]
    public void should_not_load_from_a_host_that_serves_what_anybody_uploaded_by_default(string address)
    {
        OriginPolicy.Allows(address, new BrowserOptions(), liveView: false).Should().BeFalse();
    }

    [Theory]
    [InlineData("https://accounts.example/signin", true)]
    [InlineData("https://accounts.example/", true)]
    [InlineData("http://accounts.example/signin", false)]
    [InlineData("https://evil.example/signin", false)]
    public void should_load_the_sign_in_pages_of_the_provider_only_in_a_live_view(string address, bool allowed)
    {
        OriginPolicy.Allows(address, Options(), liveView: true).Should().Be(allowed);
        OriginPolicy.Allows(address, Options(), liveView: false).Should().BeFalse();
    }

    [Fact]
    public void should_allow_plain_http_only_when_a_sandbox_says_so()
    {
        var sandbox = Options(insecure: true);
        sandbox.AssistantUrl = "http://assistant:8081/app";
        sandbox.SignInHosts = "signin";

        OriginPolicy.Allows("http://assistant:8081/auth?t=1", sandbox, liveView: false).Should().BeTrue();
        OriginPolicy.Allows("http://signin:8081/signin", sandbox, liveView: true).Should().BeTrue();
        OriginPolicy.Allows("http://signin:8081/signin", sandbox, liveView: false).Should().BeFalse();
        OriginPolicy.Allows("http://elsewhere:8081/", sandbox, liveView: true).Should().BeFalse();
        OriginPolicy.Allows("http://assistant:8081/app", Options(), liveView: true).Should().BeFalse();
    }

    [Theory]
    [InlineData("https://assistant.example/app", PageKind.Assistant)]
    [InlineData("https://assistant.example/", PageKind.Assistant)]
    [InlineData("https://accounts.example/signin?continue=x", PageKind.SignIn)]
    [InlineData("https://other.example/", PageKind.Other)]
    [InlineData("http://assistant.example/app", PageKind.Other)]
    [InlineData("about:blank", PageKind.Other)]
    [InlineData("nonsense", PageKind.Other)]
    public void should_say_what_kind_of_page_the_browser_ended_on(string address, PageKind expected)
    {
        OriginPolicy.Classify(address, Options()).Should().Be(expected);
    }

    [Theory]
    [InlineData("https://assistant.example/app", "assistant.example")]
    [InlineData("https://user:secret@assistant.example/app", null)]
    [InlineData("http://assistant.example/app", null)]
    [InlineData("nonsense", null)]
    public void should_know_the_host_of_the_assistant_only_when_the_address_may_be_used(string address, string? host)
    {
        OriginPolicy.AssistantHost(new BrowserOptions { AssistantUrl = address }).Should().Be(host);
    }

    [Fact]
    public void should_validate_the_options_and_name_the_setting()
    {
        var validator = new BrowserOptionsValidator();

        validator.Validate(null, new BrowserOptions()).Succeeded.Should().BeTrue();
        validator.Validate(null, new BrowserOptions { ProfileFolder = " " }).FailureMessage.Should().Contain("GEMINARR__BROWSER__PROFILEFOLDER");
        validator.Validate(null, new BrowserOptions { AssistantUrl = "http://assistant.example" }).FailureMessage.Should().Contain("GEMINARR__BROWSER__ASSISTANTURL");
        foreach (var (setting, value) in new[] { ("ViewportWidth", 100), ("ViewportHeight", 100), ("LiveViewMinutes", 0), ("ScreencastQuality", 101), ("SessionCheckTimeoutSeconds", 1) })
        {
            var options = new BrowserOptions();
            typeof(BrowserOptions).GetProperty(setting)!.SetValue(options, value);
            validator.Validate(null, options).FailureMessage.Should().Contain($"GEMINARR__BROWSER__{setting.ToUpperInvariant()}");
        }
    }

    [Theory]
    [InlineData("*.com")]
    [InlineData("static.example;*.org")]
    [InlineData("")]
    public void should_refuse_a_list_of_hosts_that_is_empty_or_covers_a_whole_public_suffix(string hosts)
    {
        var validator = new BrowserOptionsValidator();

        validator.Validate(null, new BrowserOptions { AllowedHosts = hosts }).Failed.Should().BeTrue();
        validator.Validate(null, new BrowserOptions { SignInHosts = hosts }).Failed.Should().BeTrue();
    }
}
