using Geminarr.Core.Identity;

namespace Geminarr.Core.Test.Identity;

public sealed class BrandingOptionsValidatorFixture
{
    private readonly BrandingOptionsValidator _subject = new();

    [Fact]
    public void should_accept_nothing_and_a_full_identity()
    {
        _subject.Validate(null, new BrandingOptions()).Succeeded.Should().BeTrue();
        _subject.Validate(null, new BrandingOptions { Name = "Lab", LogoFile = "/run/logo.png", Colors = { Name = "accent", Version = "#fff" } }).Succeeded.Should().BeTrue();
    }

    [Fact]
    public void should_accept_a_name_of_exactly_forty_characters_even_with_spaces_around_it()
    {
        _subject.Validate(null, new BrandingOptions { Name = new string('a', 40) }).Succeeded.Should().BeTrue();
        _subject.Validate(null, new BrandingOptions { Name = $"  {new string('a', 40)}  " }).Succeeded.Should().BeTrue();
    }

    [Fact]
    public void should_refuse_a_name_that_is_too_long_and_name_the_setting()
    {
        var result = _subject.Validate(null, new BrandingOptions { Name = new string('a', 41) });

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("GEMINARR__IDENTITY__");
    }

    [Theory]
    [InlineData("red", null)]
    [InlineData(null, "url(x)")]
    public void should_refuse_a_color_that_is_not_one(string? name, string? version)
    {
        var result = _subject.Validate(null, new BrandingOptions { Colors = { Name = name, Version = version } });

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("color");
    }
}
