using System.Net.WebSockets;
using System.Text;
using Geminarr.Core.Browser;
using Geminarr.Core.Connection;
using Microsoft.Extensions.Options;

namespace Geminarr.Host.Connection;

/// <summary>
/// The WebSocket of the live view (SPEC §7.3): the operator's page connects with the single-use token that an authenticated call gave it, and gets the pictures of the browser's screen; what it types and
/// clicks goes back, checked, to the browser. The session of the operator, the <c>Origin</c> of the page and the token are all checked before the browser is started. Nothing that passes is logged or kept.
/// </summary>
public static class LiveViewSocket
{
    public const string Path = "/api/v1/connection/live-views/{id}";

    /// <summary>What the page sends when the operator says they are signed in.</summary>
    private const string DoneMessage = "{\"t\":\"done\"}";

    /// <summary>How often the page the browser is on is looked at, to know when the person has signed in.</summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<WebSocket, SemaphoreSlim> Writers = [];

    private static readonly TimeSpan SignedInPoll = TimeSpan.FromSeconds(2);

    public static IEndpointRouteBuilder MapGeminarrLiveView(this IEndpointRouteBuilder endpoints)
    {
        endpoints.Map(Path, OpenAsync);
        return endpoints;
    }

    private static async Task OpenAsync(HttpContext context, string id, LiveViewService views, ConnectionService connection, IBrowserHost browser, IOptions<BrowserOptions> options, ILoggerFactory loggers)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        // A page of another origin cannot open it (a WebSocket is not bound by the same-origin policy: the server must look).
        if (!SameOrigin(context.Request))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        if (views.TryAttach(id, context.Request.Query["token"].ToString()) is not { } lease)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        using (lease)
        {
            WebSocket? socket = null;
            var signedIn = false;
            try
            {
                socket = await context.WebSockets.AcceptWebSocketAsync();
                signedIn = await RunAsync(socket, browser, connection, lease, options.Value.SignInDetection, context.RequestAborted, loggers.CreateLogger("Geminarr.LiveView"));
            }
            finally
            {
                // The live view is over whatever happened, even if the handshake failed: nobody keeps the lease. A sign-in was settled when it was made, once (a later settling would write over a disconnect).
                views.Release(lease);
                if (!signedIn)
                {
                    connection.SignInEnded(signedIn: false);
                }

                if (socket is not null)
                {
                    await CloseAsync(socket, signedIn ? "signed in" : "closed");
                    socket.Dispose();
                }
            }
        }
    }

    private static bool SameOrigin(HttpRequest request)
    {
        if (!request.Headers.TryGetValue("Origin", out var origin) || !Uri.TryCreate(origin.ToString(), UriKind.Absolute, out var uri))
        {
            return false;
        }

        // The host and the port only: behind the proxy that ends TLS the server sees http while the page is on https, and the host is what tells a page of this application from another.
        return string.Equals(uri.Authority, request.Host.Value, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Relays until the person signs in, closes it, the time is up or the connection drops. True when the session was made.</summary>
    private static async Task<bool> RunAsync(WebSocket socket, IBrowserHost browser, ConnectionService connection, LiveViewLease lease, SignInDetection detection, CancellationToken requestAborted, ILogger logger)
    {
        using var ends = CancellationTokenSource.CreateLinkedTokenSource(lease.Token, requestAborted);
        ILiveView? opened = null;
        string? failure = null;
        try
        {
            opened = await browser.OpenLiveViewAsync(ends.Token);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            failure = ex.GetType().Name;
        }

        if (opened is not { } view)
        {
            // The kind of error only: the message of a browser's error holds its command line and the addresses it was on.
            logger.LogWarning("The live view could not open the browser ({Type}).", failure);
            await SendTextAsync(socket, "{\"t\":\"error\",\"reason\":\"The browser could not be started.\"}", ends.Token);
            return false;
        }

        await using (view)
        {
            var (width, height) = view.Size;
            await SendTextAsync(socket, $"{{\"t\":\"ready\",\"width\":{width},\"height\":{height}}}", ends.Token);
            var signedIn = false;
            var pictures = PumpAsync(socket, view, ends.Token);
            var inputs = ReceiveAsync(socket, view, connection, width, height, ends.Token);
            // By default nothing is decided from the address alone: the operator says they are signed in (the message "done"), and the browser must be on the assistant's page.
            var watch = detection == SignInDetection.Page ? WatchAsync(socket, view, connection, ends.Token) : Task.Delay(Timeout.Infinite, ends.Token).ContinueWith(static _ => false, TaskScheduler.Default);
            // The first of the three to end decides: a sign-in that was said, or the end (the time is up, the page went away, the browser failed).
            var finished = await Task.WhenAny(pictures, inputs, watch);
            signedIn = finished is Task<bool> { IsCompletedSuccessfully: true, Result: true };
            await ends.CancelAsync();
            // The tasks end with the cancellation: they are awaited so that none is left to touch the socket after it is disposed. One that failed (the browser, the socket) is said by its kind, and the live view ends.
            await Task.WhenAll(pictures, inputs, watch).ContinueWith(task => FailureKinds(task, logger), TaskScheduler.Default);
            return signedIn;
        }
    }

    private static void FailureKinds(Task all, ILogger logger)
    {
        foreach (var error in (all.Exception?.InnerExceptions ?? []).Where(error => error is not (OperationCanceledException or WebSocketException)))
        {
            // The kind only: the message of a browser's error holds its command line and the addresses it was on.
            logger.LogWarning("The live view ended on an error ({Type}).", error.GetType().Name);
        }
    }

    private static async Task PumpAsync(WebSocket socket, ILiveView view, CancellationToken cancellationToken)
    {
        await foreach (var picture in view.Frames.ReadAllAsync(cancellationToken))
        {
            await SendOneAtATimeAsync(socket, picture, WebSocketMessageType.Binary, cancellationToken);
        }
    }

    /// <summary>Reads what the page sends. True when the operator said they are signed in and the browser is on the assistant's page; false when the connection ended otherwise.</summary>
    private static async Task<bool> ReceiveAsync(WebSocket socket, ILiveView view, ConnectionService connection, int width, int height, CancellationToken cancellationToken)
    {
        var buffer = new byte[LiveInputParser.MaxMessageChars * 4];
        while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            var read = await ReadMessageAsync(socket, buffer, cancellationToken);
            if (read is null)
            {
                return false;
            }

            if (read.Length > 0 && await HandleAsync(read, socket, view, connection, width, height, cancellationToken))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>One message of text: the empty string for a message that is not text (ignored), null when the page closed or sent more than a message can be.</summary>
    private static async Task<string?> ReadMessageAsync(WebSocket socket, byte[] buffer, CancellationToken cancellationToken)
    {
        var length = 0;
        ValueWebSocketReceiveResult received;
        do
        {
            received = await socket.ReceiveAsync(buffer.AsMemory(length), cancellationToken);
            length += received.Count;
            if (length >= buffer.Length)
            {
                await socket.CloseAsync(WebSocketCloseStatus.MessageTooBig, "too big", CancellationToken.None);
                return null;
            }
        }
        while (!received.EndOfMessage);

        return received.MessageType switch
        {
            WebSocketMessageType.Close => null,
            WebSocketMessageType.Text => Encoding.UTF8.GetString(buffer, 0, length),
            _ => string.Empty,
        };
    }

    /// <summary>One message: "done" (the operator says they are signed in) or an input for the browser. True when the sign-in is done.</summary>
    private static async Task<bool> HandleAsync(string message, WebSocket socket, ILiveView view, ConnectionService connection, int width, int height, CancellationToken cancellationToken)
    {
        if (message != DoneMessage)
        {
            if (LiveInputParser.Parse(message, width, height) is { } input)
            {
                await view.SendAsync(input, cancellationToken);
            }

            return false;
        }

        if (await view.CurrentPageAsync(cancellationToken) != PageKind.Assistant)
        {
            await SendTextAsync(socket, "{\"t\":\"not-yet\"}", cancellationToken);
            return false;
        }

        connection.SignInEnded(signedIn: true);
        await SendTextAsync(socket, "{\"t\":\"signed-in\"}", cancellationToken);
        return true;
    }

    /// <summary>Looks at the page the browser is on until it is the assistant's: then the session is made.</summary>
    private static async Task<bool> WatchAsync(WebSocket socket, ILiveView view, ConnectionService connection, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(SignedInPoll);
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            if (await view.CurrentPageAsync(cancellationToken) == PageKind.Assistant)
            {
                // The state is the session's before the page is told: whoever reads it next sees it connected.
                connection.SignInEnded(signedIn: true);
                await SendTextAsync(socket, "{\"t\":\"signed-in\"}", cancellationToken);
                return true;
            }
        }

        return false;
    }

    /// <summary>A WebSocket takes one send at a time, and the pictures, the answers to "done" and the signed-in message are sent from tasks of their own.</summary>
    private static async Task SendOneAtATimeAsync(WebSocket socket, ReadOnlyMemory<byte> message, WebSocketMessageType type, CancellationToken cancellationToken)
    {
        var gate = Writers.GetValue(socket, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            await socket.SendAsync(message, type, endOfMessage: true, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    private static async Task SendTextAsync(WebSocket socket, string json, CancellationToken cancellationToken)
    {
        if (socket.State == WebSocketState.Open)
        {
            await SendOneAtATimeAsync(socket, Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, cancellationToken);
        }
    }

    private static async Task CloseAsync(WebSocket socket, string reason)
    {
        try
        {
            if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, reason, CancellationToken.None);
            }
        }
        catch (WebSocketException)
        {
            // The page is gone already.
        }
    }
}
