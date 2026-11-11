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
}
