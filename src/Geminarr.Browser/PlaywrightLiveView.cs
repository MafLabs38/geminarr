using System.Text.Json;
using System.Threading.Channels;
using Geminarr.Core.Browser;
using Microsoft.Playwright;

namespace Geminarr.Browser;

/// <summary>
/// The screen of the browser as pictures (the browser's own screencast), and the keyboard and mouse that go to it. The pictures live in a channel that keeps the newest and drops what nobody read: a slow
/// viewer sees a later picture, never a growing queue. Nothing of what is shown or typed is written anywhere. Closing it closes the browser and frees it for the next use.
/// </summary>
internal sealed class PlaywrightLiveView : ILiveView
{
    private readonly BrowserSession _session;
    private readonly IPage _page;
    private readonly ICDPSession _cdp;
    private readonly BrowserOptions _options;
    private readonly SemaphoreSlim _busy;
    private readonly Channel<byte[]> _frames = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(2) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
    private int _disposed;

    private PlaywrightLiveView(BrowserSession session, IPage page, ICDPSession cdp, BrowserOptions options, SemaphoreSlim busy)
    {
        _session = session;
        _page = page;
        _cdp = cdp;
        _options = options;
        _busy = busy;
    }

    public ChannelReader<byte[]> Frames => _frames.Reader;

    public (int Width, int Height) Size => (_options.ViewportWidth, _options.ViewportHeight);

    public static async Task<PlaywrightLiveView> StartAsync(BrowserSession session, BrowserOptions options, SemaphoreSlim busy, CancellationToken cancellationToken)
    {
        var page = session.Context.Pages.FirstOrDefault() ?? await session.Context.NewPageAsync();
        var cdp = await session.Context.NewCDPSessionAsync(page);
        var view = new PlaywrightLiveView(session, page, cdp, options, busy);
        cdp.Event("Page.screencastFrame").OnEvent += (_, frame) => view.Receive(frame);
        await cdp.SendAsync("Page.startScreencast", new Dictionary<string, object>
        {
            ["format"] = "jpeg",
            ["quality"] = options.ScreencastQuality,
            ["maxWidth"] = options.ViewportWidth,
            ["maxHeight"] = options.ViewportHeight,
            ["everyNthFrame"] = 1,
        });
        // The assistant's page is opened without waiting for it: the person sees it load. A page that cannot be reached shows as an empty screen they can close.
        _ = OpenAssistantAsync(page, options);
        return view;
    }

    private static async Task OpenAssistantAsync(IPage page, BrowserOptions options)
    {
        try
        {
            await page.GotoAsync(options.AssistantUrl, new PageGotoOptions { WaitUntil = WaitUntilState.Commit, Timeout = options.SessionCheckTimeoutSeconds * 1_000 });
        }
        catch (PlaywrightException)
        {
            // A page that cannot be reached shows as an empty screen that the person can close.
        }
    }

    public async Task SendAsync(LiveInput input, CancellationToken cancellationToken)
    {
        try
        {
            await DispatchAsync(input);
        }
        catch (PlaywrightException)
        {
            // A key the browser does not know, a page that is closing: the input is lost, the live view goes on.
        }
    }

    public Task<PageKind> CurrentPageAsync(CancellationToken cancellationToken) => Task.FromResult(OriginPolicy.Classify(_page.Url, _options));

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        _frames.Writer.TryComplete();
        try
        {
            await _session.DisposeAsync();
        }
        catch (PlaywrightException)
        {
            // The browser is already gone.
        }
        finally
        {
            _busy.Release();
        }
    }

    private async Task DispatchAsync(LiveInput input)
    {
        switch (input.Kind)
        {
            case LiveInputKind.MouseMove:
                await _page.Mouse.MoveAsync((float)input.X, (float)input.Y);
                break;
            case LiveInputKind.MouseDown:
                await _page.Mouse.MoveAsync((float)input.X, (float)input.Y);
                await _page.Mouse.DownAsync(new MouseDownOptions { Button = ButtonOf(input.Button) });
                break;
            case LiveInputKind.MouseUp:
                await _page.Mouse.MoveAsync((float)input.X, (float)input.Y);
                await _page.Mouse.UpAsync(new MouseUpOptions { Button = ButtonOf(input.Button) });
                break;
            case LiveInputKind.Wheel:
                await _page.Mouse.MoveAsync((float)input.X, (float)input.Y);
                await _page.Mouse.WheelAsync((float)input.DeltaX, (float)input.DeltaY);
                break;
            case LiveInputKind.KeyDown:
                await _page.Keyboard.DownAsync(input.Key!);
                break;
            case LiveInputKind.KeyUp:
                await _page.Keyboard.UpAsync(input.Key!);
                break;
            case LiveInputKind.Text:
                await _page.Keyboard.InsertTextAsync(input.Text!);
                break;
        }
    }

    private static MouseButton ButtonOf(string button) => button switch
    {
        "right" => MouseButton.Right,
        "middle" => MouseButton.Middle,
        _ => MouseButton.Left,
    };

    /// <summary>A picture came from the browser: it is queued and acknowledged without making the event wait.</summary>
    private void Receive(JsonElement? frame) => _ = OnFrameAsync(frame);

    private async Task OnFrameAsync(JsonElement? frame)
    {
        if (frame is not { } data || !data.TryGetProperty("data", out var picture) || !data.TryGetProperty("sessionId", out var id))
        {
            return;
        }

        try
        {
            _frames.Writer.TryWrite(Convert.FromBase64String(picture.GetString() ?? string.Empty));
            await _cdp.SendAsync("Page.screencastFrameAck", new Dictionary<string, object> { ["sessionId"] = id.GetInt32() });
        }
        catch (Exception ex) when (ex is PlaywrightException or FormatException or InvalidOperationException)
        {
            // The browser is closing, or a picture was not what it should be: the next one will do.
        }
    }
}
