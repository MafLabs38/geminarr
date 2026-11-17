using System.Net.WebSockets;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Geminarr.Integration.Test.Support;

/// <summary>The web desktop of the browser container, reduced to what the relay is tested against: it serves itself under <c>/browser/</c>, sets a cookie, echoes what it was asked and what it saw, and answers a WebSocket. Synthetic.</summary>
public sealed class FakeDesktop : IAsyncDisposable
{
    private readonly WebApplication _app;

    private FakeDesktop(WebApplication app, string url)
    {
        _app = app;
        Url = url;
    }

    public string Url { get; }

    /// <summary>What the desktop was sent: the path, and the cookie and the credentials if any came with it.</summary>
    public System.Collections.Concurrent.ConcurrentQueue<(string Path, string? Cookie, string? Authorization)> Seen { get; } = new();

    /// <summary>The origin that each request came with, as the desktop saw it.</summary>
    public System.Collections.Concurrent.ConcurrentQueue<string?> Origins { get; } = new();

    /// <summary>The <c>Host</c> that each request came with, as the desktop saw it.</summary>
    public System.Collections.Concurrent.ConcurrentQueue<string> Hosts { get; } = new();

    public static async Task<FakeDesktop> StartAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Configuration["urls"] = "http://127.0.0.1:0";
        var app = builder.Build();
        FakeDesktop? desktop = null;
        app.UseWebSockets();
        app.Map("/browser/{**path}", async context =>
        {
            desktop!.Seen.Enqueue((context.Request.Path, context.Request.Headers.Cookie.ToString() is { Length: > 0 } cookie ? cookie : null, context.Request.Headers.Authorization.ToString() is { Length: > 0 } authorization ? authorization : null));
            desktop.Hosts.Enqueue(context.Request.Host.ToString());
            desktop.Origins.Enqueue(context.Request.Headers.Origin.ToString() is { Length: > 0 } origin ? origin : null);
            if (context.WebSockets.IsWebSocketRequest)
            {
                using var socket = await context.WebSockets.AcceptWebSocketAsync();
                var buffer = new byte[64];
                try
                {
                    // It echoes what it is sent until the other end closes (or the relay ends the socket): a desktop does not hang up by itself.
                    while (socket.State == WebSocketState.Open)
                    {
                        var received = await socket.ReceiveAsync(buffer, context.RequestAborted);
                        if (received.MessageType == WebSocketMessageType.Close)
                        {
                            break;
                        }

                        await socket.SendAsync(buffer.AsMemory(0, received.Count), WebSocketMessageType.Text, endOfMessage: true, context.RequestAborted);
                    }
                }
                catch (Exception ex) when (ex is WebSocketException or OperationCanceledException)
                {
                    // The other end went away.
                }

                return;
            }

            context.Response.Headers.SetCookie = "desktop=1; Path=/";
            context.Response.Headers.ContentSecurityPolicy = "default-src 'none'";
            await context.Response.WriteAsync($"desktop:{context.Request.Path}", context.RequestAborted);
        });
        await app.StartAsync();
        var url = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        desktop = new FakeDesktop(app, url);
        return desktop;
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}
