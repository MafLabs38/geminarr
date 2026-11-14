using Geminarr.Browser;
using Geminarr.Core.Browser;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Geminarr.Integration.Test;

/// <summary>
/// What the browser host decides about the profile folder without a browser: whether the folder is private, whether it holds a session, how it is made, and how it is emptied. The launch of Chromium is the
/// end-to-end test's; the rules about the files that hold the session are these, on a real folder.
/// </summary>
public sealed class ProfileFolderFixture : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("geminarr-profile-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private PlaywrightBrowserHost Subject(string folder) =>
        new(Options.Create(new BrowserOptions { ProfileFolder = folder }), NullLogger<PlaywrightBrowserHost>.Instance);

    [Fact]
    public void should_say_there_is_no_profile_when_the_folder_is_not_there()
    {
        var state = Subject(Path.Combine(_root, "missing")).Profile();

        state.Exists.Should().BeFalse();
        state.HasSession.Should().BeFalse();
    }

    [Fact]
    public void should_make_the_folder_for_its_owner_only_and_remember_that_a_person_signed_in()
    {
        var folder = Path.Combine(_root, "profile");
        var subject = Subject(folder);

        subject.MarkSignedIn();

        var state = subject.Profile();
        (state.Exists, state.HasSession, state.Private).Should().Be((true, true, true));
        if (!OperatingSystem.IsWindows())
        {
            (File.GetUnixFileMode(folder) & (UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute)).Should().Be(0);
        }
    }

    [Fact]
    public void should_say_a_folder_that_others_can_read_is_not_private_and_tighten_it_when_it_writes()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var folder = Path.Combine(_root, "loose");
        Directory.CreateDirectory(folder);
        File.SetUnixFileMode(folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
        var subject = Subject(folder);
        subject.Profile().Private.Should().BeFalse();

        subject.MarkSignedIn();

        subject.Profile().Private.Should().BeTrue("a folder that was mounted loose is tightened by the first thing written in it");
    }

    [Fact]
    public async Task should_empty_the_folder_and_keep_it_when_the_profile_is_destroyed()
    {
        var folder = Path.Combine(_root, "profile");
        var subject = Subject(folder);
        subject.MarkSignedIn();
        Directory.CreateDirectory(Path.Combine(folder, "Default"));
        await File.WriteAllTextAsync(Path.Combine(folder, "Default", "Cookies"), "x", TestContext.Current.CancellationToken);

        await subject.DestroyProfileAsync(TestContext.Current.CancellationToken);

        Directory.Exists(folder).Should().BeTrue("the folder is a mount point: its content goes, not the folder");
        Directory.EnumerateFileSystemEntries(folder).Should().BeEmpty();
        subject.Profile().HasSession.Should().BeFalse();
    }
}
