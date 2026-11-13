using Geminarr.Core.Pipeline;

namespace Geminarr.Core.Browser;

/// <summary>What the profile of the browser looks like from outside: whether the folder is there, whether it holds a session, whether only its owner can read it, and whether it can be read at all.</summary>
public sealed record ProfileState(bool Exists, bool HasSession, bool Private, bool Readable = true);

/// <summary>
/// The browser that holds the session (SPEC §7): it checks whether the session is good, opens the live view where the operator signs in, and destroys the profile. One thing at a time: a live
/// view and a check never share the profile. The implementation (Playwright, one Chromium) lives in its own project; everything that decides is in Core.
/// </summary>
public interface IBrowserHost
{
    /// <summary>The state of the profile folder, without starting a browser.</summary>
    ProfileState Profile();

    /// <summary>Remembers that a person signed in through the live view: from now on the profile holds a session, which a pass checks and which may expire.</summary>
    void MarkSignedIn();

    /// <summary>Loads the assistant's page in a browser without a window and says whether it ended on the page or on a sign-in page. A network error is a <see cref="ReaderException"/>, never "signed out".</summary>
    Task<SessionCheck> CheckSessionAsync(CancellationToken cancellationToken);

    /// <summary>Opens the browser on the assistant's address for a person to sign in through. The caller disposes it.</summary>
    Task<ILiveView> OpenLiveViewAsync(CancellationToken cancellationToken);

    /// <summary>Closes the browser and deletes what the profile holds. The folder stays (it is a mount point).</summary>
    Task DestroyProfileAsync(CancellationToken cancellationToken);
}

/// <summary>The screen of the browser, as pictures, and the keyboard and mouse that go to it.</summary>
public interface ILiveView : IAsyncDisposable
{
    /// <summary>The pictures (JPEG) of the screen, most recent last; a picture that nobody has read is dropped for a newer one.</summary>
    System.Threading.Channels.ChannelReader<byte[]> Frames { get; }

    /// <summary>The size of the screen, which input coordinates are relative to.</summary>
    (int Width, int Height) Size { get; }

    Task SendAsync(LiveInput input, CancellationToken cancellationToken);

    /// <summary>The kind of page the browser is on now: the assistant's own means the session is made.</summary>
    Task<PageKind> CurrentPageAsync(CancellationToken cancellationToken);
}
