using Geminarr.Core.Connection;
using Geminarr.Core.Pipeline;

namespace Geminarr.Core.Test.Pipeline;

public sealed class HeartbeatContentFixture
{
    private static readonly DateTimeOffset Now = new(2026, 11, 13, 9, 0, 0, TimeSpan.Zero);

    private static ConnectionRecord Record(ConnectionState state, string? reason, ConnectionBefore? before = null) => new(state, Now, Now, 2, reason, null, before);

    [Fact]
    public void should_go_on_asking_for_a_sign_in_while_the_operator_pauses()
    {
        var record = Record(ConnectionState.Paused, ConnectionReasons.Paused, new ConnectionBefore(ConnectionState.NeedsLogin, ConnectionReasons.NeedsLogin, 2));

        HeartbeatContent.AttentionOf(record, "https://geminarr.homelab.example")!.Code.Should().Be(ConnectionReasons.NeedsLogin);
        HeartbeatContent.ErrorOf(record).Should().Be("The session has expired.");
    }

    [Fact]
    public void should_ask_for_nothing_when_a_pause_took_over_a_good_session()
    {
        var record = Record(ConnectionState.Paused, ConnectionReasons.Paused, new ConnectionBefore(ConnectionState.Connected, null, 0));

        HeartbeatContent.AttentionOf(record, null).Should().BeNull();
        HeartbeatContent.ErrorOf(record).Should().BeNull();
    }

    [Fact]
    public void should_say_the_browser_could_not_be_reached_without_asking_anybody_for_anything()
    {
        var record = Record(ConnectionState.Degraded, ConnectionReasons.BrowserFailed);

        HeartbeatContent.ErrorOf(record).Should().Be("The browser of the container could not be reached.");
        HeartbeatContent.AttentionOf(record, "https://geminarr.homelab.example").Should().BeNull();
    }

    [Fact]
    public void should_say_nothing_is_wrong_when_a_pause_has_nothing_before_it()
    {
        HeartbeatContent.ErrorOf(Record(ConnectionState.Paused, ConnectionReasons.Paused)).Should().BeNull();
        HeartbeatContent.AttentionOf(Record(ConnectionState.Paused, ConnectionReasons.Paused), null).Should().BeNull();
    }
}
