using Geminarr.Core.Connection;
using Geminarr.Core.Pipeline;

namespace Geminarr.Core.Test.Pipeline;

public sealed class HeartbeatContentFixture
{
    private static ConnectionRecord Record(ConnectionState state, string? reason) => new(state, DateTimeOffset.UnixEpoch, null, 1, reason);

    [Theory]
    [InlineData(ConnectionReasons.ReaderBroken, "The assistant's page is no longer what the reader knows.")]
    [InlineData(ConnectionReasons.ExtensionSilent, "The browser's extension has not said anything for a while.")]
    [InlineData(ConnectionReasons.RateLimited, "The assistant asked to slow down.")]
    [InlineData(ConnectionReasons.Unreachable, "The assistant could not be reached.")]
    [InlineData("something-new", "The last pass failed.")]
    public void should_say_in_one_sentence_why_a_degraded_connection_is(string reason, string sentence)
    {
        HeartbeatContent.ErrorOf(Record(ConnectionState.Degraded, reason)).Should().Be(sentence);
    }

    [Fact]
    public void should_ask_for_a_person_only_for_the_two_codes_that_mnemarr_knows()
    {
        HeartbeatContent.AttentionOf(Record(ConnectionState.Degraded, ConnectionReasons.ExtensionSilent), "https://geminarr.homelab.example").Should().BeNull("a silent browser is a sentence, not a mail (SPEC Q-14)");
        HeartbeatContent.AttentionOf(Record(ConnectionState.Degraded, ConnectionReasons.ReaderBroken), "https://geminarr.homelab.example")!.Code.Should().Be(ConnectionReasons.ReaderBroken);
        HeartbeatContent.AttentionOf(Record(ConnectionState.NeedsLogin, ConnectionReasons.NeedsLogin), "https://geminarr.homelab.example")!.Code.Should().Be(ConnectionReasons.NeedsLogin);
    }
}
