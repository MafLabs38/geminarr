using Geminarr.Core.Access;

namespace Geminarr.Core.Test.Access;

public sealed class OperatorCredentialsFixture : IDisposable
{
    private const string Password = "a long enough password";

    private readonly string _folder = Directory.CreateTempSubdirectory("geminarr-operator-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private string File(string content)
    {
        var path = Path.Combine(_folder, $"password-{Guid.NewGuid():N}");
        System.IO.File.WriteAllText(path, content);
        return path;
    }

    private static OperatorCredentials Subject(string? name, string? file) =>
        new(Options.Of(new AuthOptions { AdminUser = name, AdminPasswordFile = file }));

    [Fact]
    public void should_be_ready_when_the_name_and_a_long_enough_password_are_there()
    {
        Subject("operator-name", File(Password)).Status.Should().Be(OperatorStatus.Ready);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("operator-name", null)]
    [InlineData(null, "some-file")]
    public void should_be_not_configured_when_the_name_or_the_file_is_missing(string? name, string? file)
    {
        Subject(name, file).Status.Should().Be(OperatorStatus.NotConfigured);
    }

    [Fact]
    public void should_say_the_password_is_unreadable_when_the_file_is_missing_or_empty()
    {
        Subject("operator-name", Path.Combine(_folder, "missing")).Status.Should().Be(OperatorStatus.PasswordUnreadable);
        Subject("operator-name", File("\n")).Status.Should().Be(OperatorStatus.PasswordUnreadable);
    }

    [Fact]
    public void should_say_the_password_is_weak_when_it_is_shorter_than_the_minimum()
    {
        Subject("operator-name", File(new string('x', OperatorCredentials.MinPasswordLength - 1))).Status.Should().Be(OperatorStatus.PasswordWeak);
    }

    [Theory]
    [InlineData("operator-name", Password, true)]
    [InlineData("operator-name", "a long enough passworD", false)]
    [InlineData("Operator-name", Password, false)]
    [InlineData("someone-else", Password, false)]
    [InlineData("", "", false)]
    public void should_accept_only_the_exact_name_and_password(string name, string password, bool expected)
    {
        Subject("operator-name", File(Password)).Verify(name, password).Should().Be(expected);
    }

    [Fact]
    public void should_ignore_the_line_break_that_an_editor_or_echo_adds()
    {
        Subject("operator-name", File(Password + "\n")).Verify("operator-name", Password).Should().BeTrue();
        Subject("operator-name", File(Password + "\r\n")).Verify("operator-name", Password).Should().BeTrue();
    }

    [Fact]
    public void should_read_the_file_at_each_sign_in_so_that_a_rotated_secret_needs_no_restart()
    {
        var path = File(Password);
        var subject = Subject("operator-name", path);
        subject.Verify("operator-name", Password).Should().BeTrue();

        System.IO.File.WriteAllText(path, "another long password");

        subject.Verify("operator-name", Password).Should().BeFalse();
        subject.Verify("operator-name", "another long password").Should().BeTrue();
    }

    [Fact]
    public void should_never_accept_anything_when_nobody_can_sign_in()
    {
        var weak = Subject("operator-name", File("short"));
        var none = Subject(null, null);

        weak.Verify("operator-name", "short").Should().BeFalse("a password that is too short is refused, not accepted");
        none.Verify(string.Empty, string.Empty).Should().BeFalse();
        none.Verify("operator-name", Password).Should().BeFalse();
    }

    [Fact]
    public void should_trim_the_configured_name()
    {
        Subject("  operator-name  ", File(Password)).Name.Should().Be("operator-name");
    }
}
