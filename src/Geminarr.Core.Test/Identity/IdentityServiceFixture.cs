using Geminarr.Core.Identity;
using Geminarr.Core.Test.Access;

namespace Geminarr.Core.Test.Identity;

public sealed class IdentityServiceFixture : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("geminarr-identity-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private string Write(string name, byte[] content)
    {
        var path = Path.Combine(_folder, name);
        File.WriteAllBytes(path, content);
        return path;
    }

    private static IdentityService Subject(IdentityOptions options) => new(Options.Of(options));

    [Fact]
    public void should_be_the_product_own_when_the_deployment_says_nothing()
    {
        var identity = Subject(new IdentityOptions()).Get();

        identity.Name.Should().Be("Geminarr");
        identity.Colors.Should().Be(new IdentityColors("accent", "accent"));
        identity.Logo.Should().BeNull();
        identity.LogoProblem.Should().BeNull();
    }

    [Fact]
    public void should_take_what_the_deployment_says()
    {
        var path = Write("logo.png", IdentityRulesFixture.Png);

        var identity = Subject(new IdentityOptions { Name = " Lab ", LogoFile = path, Colors = { Name = "#3ee0c5", Version = "inkMuted" } }).Get();

        identity.Name.Should().Be("Lab");
        identity.Colors.Should().Be(new IdentityColors("#3ee0c5", "inkMuted"));
        identity.Logo!.ContentType.Should().Be("image/png");
        identity.Logo.Hash.Should().HaveLength(64);
        identity.Logo.Content.Should().Equal(IdentityRulesFixture.Png);
    }

    [Fact]
    public void should_say_why_a_logo_is_not_used_and_never_where_it_is()
    {
        var missing = Subject(new IdentityOptions { LogoFile = Path.Combine(_folder, "missing.png") }).Get();
        var notAnImage = Subject(new IdentityOptions { LogoFile = Write("fake.png", "not a png"u8.ToArray()) }).Get();
        var tooLarge = Subject(new IdentityOptions { LogoFile = Write("large.png", new byte[IdentityRules.MaxLogoBytes + 1]) }).Get();
        var badPath = Subject(new IdentityOptions { LogoFile = "bad\0path" }).Get();

        new[] { missing, notAnImage, tooLarge, badPath }.Should().OnlyContain(identity => identity.Logo == null && identity.LogoProblem != null && !identity.LogoProblem.Contains(_folder));
        missing.LogoProblem.Should().Contain("not there");
        notAnImage.LogoProblem.Should().Contain("not used");
        tooLarge.LogoProblem.Should().Contain("larger");
        badPath.LogoProblem.Should().Contain("cannot be read");
    }

    [Fact]
    public void should_read_the_file_again_only_when_it_changed()
    {
        var path = Write("logo.png", IdentityRulesFixture.Png);
        var subject = Subject(new IdentityOptions { LogoFile = path });
        var first = subject.Get().Logo!;

        subject.Get().Logo.Should().BeSameAs(first);

        var other = IdentityRulesFixture.Png.Append((byte)1).ToArray();
        File.WriteAllBytes(path, other);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(1));

        var second = subject.Get().Logo!;
        second.Hash.Should().NotBe(first.Hash);
        second.Content.Should().Equal(other);
    }

    [Fact]
    public void should_notice_that_the_file_has_gone()
    {
        var path = Write("logo.png", IdentityRulesFixture.Png);
        var subject = Subject(new IdentityOptions { LogoFile = path });
        subject.Get().Logo.Should().NotBeNull();

        File.Delete(path);

        subject.Get().LogoProblem.Should().Contain("not there");
    }

    [Fact]
    public void should_drop_a_logo_that_was_replaced_by_a_file_that_is_not_an_image()
    {
        var path = Write("logo.png", IdentityRulesFixture.Png);
        var subject = Subject(new IdentityOptions { LogoFile = path });
        subject.Get().Logo.Should().NotBeNull();

        File.WriteAllBytes(path, "not a png"u8.ToArray());
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(1));

        var identity = subject.Get();
        identity.Logo.Should().BeNull();
        identity.LogoProblem.Should().Contain("not used");
    }
}
