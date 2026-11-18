using Geminarr.Core.Extension;
using Geminarr.Core.Test.Access;

namespace Geminarr.Core.Test.Extension;

public sealed class ExtensionTokenFixture : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("geminarr-token-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private ExtensionToken With(string? content, bool fileExists = true)
    {
        var path = Path.Combine(_folder, "token");
        if (content is not null && fileExists)
        {
            File.WriteAllText(path, content);
        }

        return new ExtensionToken(Options.Of(new ExtensionOptions { TokenFile = content is null && !fileExists ? null : path }));
    }

    [Fact]
    public void should_be_not_configured_without_a_file()
    {
        var subject = With(null, fileExists: false);

        subject.Status.Should().Be(ExtensionTokenStatus.NotConfigured);
        subject.Verify("anything at all, long enough to be a token").Should().BeFalse();
    }

    [Fact]
    public void should_accept_nothing_when_the_file_that_is_named_is_missing()
    {
        var subject = new ExtensionToken(Options.Of(new ExtensionOptions { TokenFile = Path.Combine(_folder, "missing") }));

        subject.Verify("a token that is long enough to be one, but nobody gave it").Should().BeFalse();
    }

    [Fact]
    public void should_say_unreadable_for_a_file_that_is_missing_or_empty()
    {
        new ExtensionToken(Options.Of(new ExtensionOptions { TokenFile = Path.Combine(_folder, "missing") })).Status.Should().Be(ExtensionTokenStatus.Unreadable);
        With("\n").Status.Should().Be(ExtensionTokenStatus.Unreadable);
    }

    [Fact]
    public void should_refuse_a_file_that_is_not_a_token_by_its_size()
    {
        With(new string('a', ExtensionToken.MaxFileBytes + 1)).Status.Should().Be(ExtensionTokenStatus.Unreadable);
    }

    [Fact]
    public void should_say_weak_for_a_token_that_is_too_short_and_never_accept_it()
    {
        var subject = With("short-token");

        subject.Status.Should().Be(ExtensionTokenStatus.Weak);
        subject.Verify("short-token").Should().BeFalse("a short token is refused rather than accepted");
    }

    [Fact]
    public void should_accept_the_token_without_the_line_break_an_editor_adds_and_nothing_else()
    {
        var token = new string('k', ExtensionToken.MinLength);
        var subject = With(token + "\r\n");

        subject.Status.Should().Be(ExtensionTokenStatus.Ready);
        subject.Verify(token).Should().BeTrue();
        subject.Verify(token + "x").Should().BeFalse();
        subject.Verify(token[..^1]).Should().BeFalse();
        subject.Verify(null).Should().BeFalse();
        subject.Verify(string.Empty).Should().BeFalse();
    }

    [Fact]
    public void should_read_the_file_again_each_time_so_that_rotating_the_secret_needs_no_restart()
    {
        var first = new string('a', ExtensionToken.MinLength);
        var second = new string('b', ExtensionToken.MinLength);
        var subject = With(first);
        subject.Verify(first).Should().BeTrue();

        File.WriteAllText(Path.Combine(_folder, "token"), second);

        subject.Verify(first).Should().BeFalse();
        subject.Verify(second).Should().BeTrue();
    }
}
