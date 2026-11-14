using Geminarr.Core.Browser;
using Geminarr.Core.Connection;
using Geminarr.Core.Pipeline;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;

namespace Geminarr.Browser;

/// <summary>
/// The one Chromium of Geminarr (SPEC §7, D-08, D-09), driven with Playwright, on a profile folder of its own. It does one thing at a time: a live view holds the browser until it is closed, and a check
/// of the session waits for nothing (it says the browser is busy). Every request the browser makes goes through <see cref="OriginPolicy"/>: another origin than the assistant's is refused, and the
/// account provider's sign-in pages load only in a live view. The profile folder is private to its owner and holds no extension, no sync and no saved password.
/// </summary>
public sealed class PlaywrightBrowserHost : IBrowserHost, IAsyncDisposable
{
    /// <summary>The file that says a person signed in: the profile holds a session, which a pass checks and which may expire.</summary>
    public const string SignedInMarker = ".geminarr-signed-in";

    private const UnixFileMode GroupAndOther = UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;

    private static readonly string[] Arguments =
    [
        "--disable-sync",
        "--disable-extensions",
        "--no-first-run",
        "--no-default-browser-check",
        "--disable-dev-shm-usage",
        "--disable-background-networking",
        "--disable-save-password-bubble",
        "--password-store=basic",
        "--disable-features=Translate,AutofillServerCommunication,MediaRouter",
    ];

    private readonly IOptions<BrowserOptions> _options;
    private readonly ILogger<PlaywrightBrowserHost> _logger;
    private readonly SemaphoreSlim _busy = new(1, 1);
    private IPlaywright? _playwright;

    public PlaywrightBrowserHost(IOptions<BrowserOptions> options, ILogger<PlaywrightBrowserHost> logger)
    {
        _options = options;
        _logger = logger;
    }

    private string Folder => _options.Value.ProfileFolder;

    public ProfileState Profile()
    {
        try
        {
            if (!Directory.Exists(Folder))
            {
                return new ProfileState(Exists: false, HasSession: false, Private: true);
            }

            var isPrivate = OperatingSystem.IsWindows() || (File.GetUnixFileMode(Folder) & GroupAndOther) == 0;
            return new ProfileState(Exists: true, HasSession: File.Exists(Path.Combine(Folder, SignedInMarker)), isPrivate);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new ProfileState(Exists: true, HasSession: false, Private: true, Readable: false);
        }
    }

    public void MarkSignedIn()
    {
        EnsureFolder();
        File.WriteAllText(Path.Combine(Folder, SignedInMarker), DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
    }

    public async Task<SessionCheck> CheckSessionAsync(CancellationToken cancellationToken)
    {
        if (!await _busy.WaitAsync(0, cancellationToken))
        {
            throw new ReaderException(ConnectionReasons.Unreachable, "The browser is busy: a person is signing in.");
        }

        try
        {
            await using var session = await LaunchAsync(liveView: false);
            return await session.CheckAsync(cancellationToken);
        }
        finally
        {
            _busy.Release();
        }
    }

    public async Task<ILiveView> OpenLiveViewAsync(CancellationToken cancellationToken)
    {
        await _busy.WaitAsync(cancellationToken);
        BrowserSession? session = null;
        try
        {
            session = await LaunchAsync(liveView: true);
            return await PlaywrightLiveView.StartAsync(session, _options.Value, _busy, cancellationToken);
        }
        catch
        {
            // A browser that was started and not handed over is closed, or it would hold the profile for the next launch.
            if (session is not null)
            {
                await session.DisposeAsync();
            }

            _busy.Release();
            throw;
        }
    }

    public async Task DestroyProfileAsync(CancellationToken cancellationToken)
    {
        await _busy.WaitAsync(cancellationToken);
        try
        {
            if (Directory.Exists(Folder))
            {
                // The folder is a mount point: its content goes, not the folder.
                foreach (var entry in Directory.EnumerateFileSystemEntries(Folder))
                {
                    if (Directory.Exists(entry))
                    {
                        Directory.Delete(entry, recursive: true);
                    }
                    else
                    {
                        File.Delete(entry);
                    }
                }
            }

            EnsureFolder();
        }
        finally
        {
            _busy.Release();
        }
    }

    public ValueTask DisposeAsync()
    {
        _playwright?.Dispose();
        return ValueTask.CompletedTask;
    }

    private void EnsureFolder()
    {
        if (!Directory.Exists(Folder))
        {
            CreateFolder();
        }
        else if (!OperatingSystem.IsWindows() && (File.GetUnixFileMode(Folder) & GroupAndOther) != 0)
        {
            File.SetUnixFileMode(Folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    /// <summary>The folder of a profile is made for its owner only; a volume that was mounted is not changed by this (it exists).</summary>
    private void CreateFolder()
    {
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(Folder);
            return;
        }

        Directory.CreateDirectory(Folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    private async Task<BrowserSession> LaunchAsync(bool liveView)
    {
        EnsureFolder();
        _playwright ??= await Playwright.CreateAsync();
        var options = _options.Value;
        string failure;
        try
        {
            var context = await _playwright.Chromium.LaunchPersistentContextAsync(Folder, new BrowserTypeLaunchPersistentContextOptions
            {
                Headless = true,
                ExecutablePath = string.IsNullOrWhiteSpace(options.ExecutablePath) ? null : options.ExecutablePath,
                ViewportSize = new ViewportSize { Width = options.ViewportWidth, Height = options.ViewportHeight },
                Args = Arguments,
                AcceptDownloads = false,
                ServiceWorkers = ServiceWorkerPolicy.Block,
                Locale = "en-US",
                ChromiumSandbox = options.ChromiumSandbox,
            });
            var session = new BrowserSession(context, options, liveView, _logger);
            await session.FilterAsync();
            return session;
        }
        catch (PlaywrightException ex)
        {
            failure = ex.GetType().Name;
        }

        // Never the message: it can hold the path of the browser or of the profile.
        _logger.LogError("The browser could not be started ({Type}).", failure);
        throw new ReaderException(ConnectionReasons.ReaderBroken, "The browser could not be started.");
    }
}
