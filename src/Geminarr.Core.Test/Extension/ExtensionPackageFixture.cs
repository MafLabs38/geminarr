using Geminarr.Core.Extension;
using Geminarr.Core.Test.Access;

namespace Geminarr.Core.Test.Extension;

public sealed class ExtensionPackageFixture : IDisposable
{
    private const string Id = "abcdefghijklmnopabcdefghijklmnop";
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"geminarr-package-{Guid.NewGuid():N}");

    public ExtensionPackageFixture()
    {
        Directory.CreateDirectory(_folder);
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private ExtensionPackage Subject() => new(Options.Of(new ExtensionOptions { PackageFolder = _folder }));

    private void Describe(string json, string? file = "geminarr-reader-1.0.0.crx")
    {
        File.WriteAllText(Path.Combine(_folder, ExtensionPackage.DescriptionFile), json);
        if (file is not null)
        {
            File.WriteAllBytes(Path.Combine(_folder, file), [1, 2, 3]);
        }
    }

    [Fact]
    public void should_find_nothing_in_a_folder_that_has_no_description()
    {
        Subject().Current().Should().BeNull();
    }

    [Fact]
    public void should_find_the_package_that_the_description_names()
    {
        Describe($$"""{ "id": "{{Id}}", "version": "1.0.0", "file": "geminarr-reader-1.0.0.crx" }""");

        Subject().Current().Should().Be(new ExtensionPackageInfo(Id, "1.0.0", "geminarr-reader-1.0.0.crx", Path.Combine(_folder, "geminarr-reader-1.0.0.crx")));
    }

    [Theory]
    [InlineData("""{ "id": "short", "version": "1.0.0", "file": "a.crx" }""")]
    [InlineData("""{ "id": "abcdefghijklmnopabcdefghijklmnop", "version": "one", "file": "a.crx" }""")]
    [InlineData("""{ "id": "abcdefghijklmnopabcdefghijklmnop", "version": "1.0.0", "file": "../a.crx" }""")]
    [InlineData("""{ "id": "abcdefghijklmnopabcdefghijklmnop", "version": "1.0.0", "file": "a..b.crx" }""")]
    [InlineData("""{ "id": "abcdefghijklmnopabcdefghijklmnop", "version": "1.0.0", "file": "a.zip" }""")]
    [InlineData("""{ "id": "abcdefghijklmnopabcdefghijklmnop", "version": "1.0.0" }""")]
    [InlineData("not json")]
    [InlineData("null")]
    public void should_take_a_description_that_is_not_what_the_pipeline_writes_for_no_package(string json)
    {
        Describe(json, file: "a.crx");

        Subject().Current().Should().BeNull();
    }

    [Fact]
    public void should_find_nothing_when_the_file_that_is_named_is_missing_or_the_description_is_huge()
    {
        Describe($$"""{ "id": "{{Id}}", "version": "1.0.0", "file": "missing.crx" }""", file: null);
        Subject().Current().Should().BeNull();

        Describe(new string(' ', 5_000) + $$"""{ "id": "{{Id}}", "version": "1.0.0", "file": "a.crx" }""");
        Subject().Current().Should().BeNull();
    }
}

public sealed class ExtensionPublicUrlFixture
{
    [Theory]
    [InlineData("https://geminarr.homelab.example")]
    [InlineData("https://geminarr.homelab.example/")]
    [InlineData("https://server-1:8282")]
    [InlineData(null)]
    [InlineData("")]
    public void should_accept_an_address_without_a_path(string? url)
    {
        new ExtensionOptionsValidator().Validate(null, new ExtensionOptions { PublicUrl = url }).Succeeded.Should().BeTrue();
    }

    [Theory]
    [InlineData("geminarr.homelab.example")]
    [InlineData("ftp://geminarr.homelab.example")]
    [InlineData("https://geminarr.homelab.example/path")]
    [InlineData("https://geminarr.homelab.example/?a=b")]
    [InlineData("https://user:secret@geminarr.homelab.example")]
    public void should_refuse_what_is_not_an_address_to_build_on_and_name_the_setting(string url)
    {
        var result = new ExtensionOptionsValidator().Validate(null, new ExtensionOptions { PublicUrl = url });

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("GEMINARR__EXTENSION__PUBLICURL").And.NotContain("secret");
    }
}
