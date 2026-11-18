using Geminarr.Core.Extension;

namespace Geminarr.Core.Test.Extension;

public sealed class ExtensionOptionsValidatorFixture
{
    private static Microsoft.Extensions.Options.ValidateOptionsResult Validate(ExtensionOptions options) => new ExtensionOptionsValidator().Validate(null, options);

    [Theory]
    [InlineData(1)]
    [InlineData(45)]
    [InlineData(1_440)]
    public void should_accept_a_silence_inside_the_bounds(int minutes)
    {
        Validate(new ExtensionOptions { SilentAfterMinutes = minutes }).Succeeded.Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1_441)]
    public void should_refuse_a_silence_outside_the_bounds_and_name_the_setting(int minutes)
    {
        var result = Validate(new ExtensionOptions { SilentAfterMinutes = minutes });

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("GEMINARR__EXTENSION__SILENTAFTERMINUTES");
    }

    [Fact]
    public void should_refuse_an_empty_package_folder_and_accept_the_defaults()
    {
        Validate(new ExtensionOptions { PackageFolder = " " }).Failed.Should().BeTrue();
        Validate(new ExtensionOptions()).Succeeded.Should().BeTrue();
    }

    [Theory]
    [InlineData("https://geminarr.homelab.example")]
    [InlineData("https://geminarr.homelab.example/")]
    [InlineData("https://server-1:8282")]
    [InlineData(null)]
    [InlineData("")]
    public void should_accept_a_public_address_without_a_path(string? url)
    {
        Validate(new ExtensionOptions { PublicUrl = url }).Succeeded.Should().BeTrue();
    }

    [Theory]
    [InlineData("geminarr.homelab.example")]
    [InlineData("ftp://geminarr.homelab.example")]
    [InlineData("https://geminarr.homelab.example/path")]
    [InlineData("https://geminarr.homelab.example/?a=b")]
    [InlineData("https://user:secret@geminarr.homelab.example")]
    public void should_refuse_what_is_not_an_address_to_build_on_and_name_the_setting(string url)
    {
        var result = Validate(new ExtensionOptions { PublicUrl = url });

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("GEMINARR__EXTENSION__PUBLICURL").And.NotContain("secret");
    }
}
