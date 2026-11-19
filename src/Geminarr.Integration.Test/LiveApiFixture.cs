using System.Net;
using Geminarr.Host.Live;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Geminarr.Integration.Test;

/// <summary>The channel that tells the screens that something changed (ADR 0023): closed to whoever is not signed in, and sending a topic, never data, once per burst.</summary>
public sealed class LiveApiFixture
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task should_refuse_the_channel_without_a_sign_in_and_open_it_with_one()
    {
        using var factory = new GeminarrFactory();
        using var anonymous = factory.CreateClient();
        anonymous.DefaultRequestHeaders.Add("X-Geminarr-Request", "live");
        using var signedIn = await factory.SignedInClientAsync();

        (await anonymous.PostAsync($"{LiveHub.Path}/negotiate?negotiateVersion=1", null, Token)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await signedIn.PostAsync($"{LiveHub.Path}/negotiate?negotiateVersion=1", null, Token)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task should_send_each_topic_once_for_a_burst_and_nothing_but_the_topic()
    {
        var proxy = Substitute.For<IClientProxy>();
        var clients = Substitute.For<IHubClients>();
        clients.All.Returns(proxy);
        var hub = Substitute.For<IHubContext<LiveHub>>();
        hub.Clients.Returns(clients);
        var subject = new LiveBroadcaster(hub, NullLogger<LiveBroadcaster>.Instance, new FakeTimeProvider());

        subject.Publish("connection");
        subject.Publish("connection");
        subject.Publish("runs");
        await subject.FlushAsync(Token);
        await subject.FlushAsync(Token);

        await proxy.Received(1).SendCoreAsync(LiveHub.Changed, Arg.Is<object?[]>(arguments => arguments.SequenceEqual(new object[] { "connection" })), Token);
        await proxy.Received(1).SendCoreAsync(LiveHub.Changed, Arg.Is<object?[]>(arguments => arguments.SequenceEqual(new object[] { "runs" })), Token);
        proxy.ReceivedCalls().Should().HaveCount(2, "the second flush had nothing to say");
    }

    [Fact]
    public async Task should_not_let_a_client_that_went_away_hold_up_the_others()
    {
        var proxy = Substitute.For<IClientProxy>();
        proxy.SendCoreAsync(Arg.Any<string>(), Arg.Any<object?[]>(), Arg.Any<CancellationToken>()).Returns(Task.FromException(new InvalidOperationException("gone")));
        var clients = Substitute.For<IHubClients>();
        clients.All.Returns(proxy);
        var hub = Substitute.For<IHubContext<LiveHub>>();
        hub.Clients.Returns(clients);
        var subject = new LiveBroadcaster(hub, NullLogger<LiveBroadcaster>.Instance, new FakeTimeProvider());

        subject.Publish("connection");
        subject.Publish("status");
        var flush = () => subject.FlushAsync(Token);

        await flush.Should().NotThrowAsync();
        proxy.ReceivedCalls().Should().HaveCount(2, "the second topic was sent although the first failed");
    }
}
