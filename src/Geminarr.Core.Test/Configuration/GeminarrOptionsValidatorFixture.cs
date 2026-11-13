using Geminarr.Core.Configuration;

namespace Geminarr.Core.Test.Configuration;

public sealed class GeminarrOptionsValidatorFixture
{
    private readonly GeminarrOptionsValidator _subject = new();

    [Fact]
    public void should_accept_the_default_settings()
    {
        _subject.Validate(null, new GeminarrOptions()).Succeeded.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void should_refuse_to_start_and_name_the_setting_when_the_data_folder_is_blank(string folder)
    {
        var result = _subject.Validate(null, new GeminarrOptions { DataFolder = folder });

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("GEMINARR__DATAFOLDER");
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ;, ")]
    public void should_refuse_a_list_of_hosts_with_no_name_in_it(string hosts)
    {
        var result = _subject.Validate(null, new GeminarrOptions { AllowedHostNames = hosts });

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("GEMINARR__ALLOWEDHOSTNAMES");
    }

    [Theory]
    [InlineData("https://geminarr.homelab.example")]
    [InlineData("http://192.168.1.10:8282")]
    public void should_accept_a_public_address_that_is_an_http_address_with_a_host(string address)
    {
        _subject.Validate(null, new GeminarrOptions { PublicUrl = address }).Succeeded.Should().BeTrue();
    }

    [Theory]
    [InlineData("geminarr.homelab.example")]
    [InlineData("ftp://geminarr.homelab.example")]
    [InlineData("https://user:secret@geminarr.homelab.example")]
    public void should_refuse_a_public_address_that_is_not_one_and_name_the_setting(string address)
    {
        var result = _subject.Validate(null, new GeminarrOptions { PublicUrl = address });

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("GEMINARR__PUBLICURL");
    }
}
