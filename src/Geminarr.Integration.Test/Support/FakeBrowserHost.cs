using System.Threading.Channels;
using Geminarr.Core.Browser;
using Geminarr.Core.Pipeline;

namespace Geminarr.Integration.Test.Support;

/// <summary>A browser in memory for the tests of the host: the profile and the page it is on are what the test says, and the pictures are what the test shows.</summary>
public sealed class FakeBrowserHost : IBrowserHost
{
    private ProfileState _state = new(Exists: true, HasSession: false, Private: true);

    public FakeLiveView? View { get; private set; }

    public int Destroyed { get; private set; }

    public bool FailToOpen { get; set; }

    public ProfileState Profile() => _state;

    public void MarkSignedIn() => _state = _state with { HasSession = true };

    public Task<SessionCheck> CheckSessionAsync(CancellationToken cancellationToken) => Task.FromResult(SessionCheck.SignedIn);

    public Task<ILiveView> OpenLiveViewAsync(CancellationToken cancellationToken)
    {
        if (FailToOpen)
        {
            throw new InvalidOperationException("The browser could not be started.");
        }

        View = new FakeLiveView();
        return Task.FromResult<ILiveView>(View);
    }

    public Task DestroyProfileAsync(CancellationToken cancellationToken)
    {
        Destroyed++;
        _state = new ProfileState(Exists: true, HasSession: false, Private: true);
        return Task.CompletedTask;
    }
}

public sealed class FakeLiveView : ILiveView
{
    private readonly Channel<byte[]> _frames = Channel.CreateUnbounded<byte[]>();
    private readonly List<LiveInput> _inputs = [];
    private readonly Lock _gate = new();

    public ChannelReader<byte[]> Frames => _frames.Reader;

    public (int Width, int Height) Size => (1280, 800);

    public PageKind Page { get; set; } = PageKind.SignIn;

    public bool Disposed { get; private set; }

    public void Show(byte[] picture) => _frames.Writer.TryWrite(picture);

    public IReadOnlyList<LiveInput> Inputs()
    {
        lock (_gate)
        {
            return [.. _inputs];
        }
    }

    public Task SendAsync(LiveInput input, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _inputs.Add(input);
        }

        return Task.CompletedTask;
    }

    public Task<PageKind> CurrentPageAsync(CancellationToken cancellationToken) => Task.FromResult(Page);

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        _frames.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }
}
