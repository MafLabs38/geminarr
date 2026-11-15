using Geminarr.Core.Browser;

namespace Geminarr.Core.Test.Browser;

public sealed class OriginPolicyFixture
{
    private static BrowserOptions Options(bool insecure = false) => new()
    {
        AssistantUrl = "https://assistant.example/app",
        SignInHosts = "accounts.example",
        AllowInsecureAssistant = insecure,
    };

    [Theory]
    [InlineData("https://assistant.example/app", PageKind.Assistant)]
    [InlineData("https://assistant.example/anything?x=1", PageKind.Assistant)]
    [InlineData("https://ASSISTANT.example/app", PageKind.Assistant)]
    [InlineData("https://accounts.example/signin", PageKind.SignIn)]
    [InlineData("https://accounts.example/", PageKind.SignIn)]
    [InlineData("https://other.example/", PageKind.Other)]
    [InlineData("https://assistant.example.evil.example/", PageKind.Other)]
    [InlineData("https://evil.example/assistant.example", PageKind.Other)]
    [InlineData("http://assistant.example/app", PageKind.Other)]
    [InlineData("chrome-error://chromewebdata/", PageKind.Other)]
    [InlineData("about:blank", PageKind.Other)]
    [InlineData("not an address", PageKind.Other)]
    public void should_tell_the_assistants_page_from_a_sign_in_page_and_from_the_rest(string address, PageKind expected)
    {
        OriginPolicy.Classify(address, Options()).Should().Be(expected);
    }

    [Fact]
    public void should_read_plain_http_only_when_a_sandbox_says_so()
    {
        var sandbox = Options(insecure: true);
        sandbox.AssistantUrl = "http://assistant:8081/app";
        sandbox.SignInHosts = "signin";

        OriginPolicy.Classify("http://assistant:8081/auth", sandbox).Should().Be(PageKind.Assistant);
        OriginPolicy.Classify("http://signin:8081/signin", sandbox).Should().Be(PageKind.SignIn);
        OriginPolicy.Classify("http://assistant:8081/app", Options()).Should().Be(PageKind.Other);
    }

    [Theory]
    [InlineData("https://assistant.example/app", true)]
    [InlineData("http://assistant.example/app", false)]
    [InlineData("not an address", false)]
    [InlineData("https://user:secret@assistant.example/app", false)]
    public void should_accept_as_the_assistant_only_an_https_address_with_a_host_and_no_credentials(string address, bool accepted)
    {
        var options = Options();
        options.AssistantUrl = address;

        (OriginPolicy.AssistantHost(options) is not null).Should().Be(accepted);
    }
}
