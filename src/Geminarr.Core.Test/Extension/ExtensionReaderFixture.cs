using Geminarr.Core.Connection;
using Geminarr.Core.Extension;
using Geminarr.Core.Pipeline;
using Geminarr.Core.Test.Access;
using Microsoft.Extensions.Time.Testing;

namespace Geminarr.Core.Test.Extension;

public sealed class ExtensionReaderFixture
{
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 18, 9, 0, 0, TimeSpan.Zero));
    private readonly ExtensionInbox _inbox = new();

    private ExtensionReader Subject(int silentAfterMinutes = 45) => new(_inbox, Options.Of(new ExtensionOptions { SilentAfterMinutes = silentAfterMinutes }), _clock);

    [Fact]
    public async Task should_say_the_web_is_its_source()
    {
        Subject().Source.Should().Be("web");
        await Task.CompletedTask;
    }

    [Fact]
    public async Task should_not_take_an_extension_that_never_spoke_for_a_session_that_is_gone()
    {
        var check = () => Subject().CheckSessionAsync(TestContext.Current.CancellationToken);

        (await check.Should().ThrowAsync<ReaderException>()).Which.Reason.Should().Be(ConnectionReasons.ExtensionSilent);
    }

    [Fact]
    public async Task should_say_signed_in_or_signed_out_as_the_extension_said_it()
    {
        _inbox.RecordSession(signedIn: true, "1.0.0", _clock.GetUtcNow());
        (await Subject().CheckSessionAsync(TestContext.Current.CancellationToken)).Should().Be(SessionCheck.SignedIn);

        _inbox.RecordSession(signedIn: false, "1.0.0", _clock.GetUtcNow());
        (await Subject().CheckSessionAsync(TestContext.Current.CancellationToken)).Should().Be(SessionCheck.SignedOut);
    }

    [Fact]
    public async Task should_take_a_silent_extension_for_a_reader_that_cannot_say_and_not_for_a_signed_out_session()
    {
        _inbox.RecordSession(signedIn: true, null, _clock.GetUtcNow());
        _clock.Advance(TimeSpan.FromMinutes(44));
        (await Subject().CheckSessionAsync(TestContext.Current.CancellationToken)).Should().Be(SessionCheck.SignedIn, "within the time it is still the word of the extension");

        _clock.Advance(TimeSpan.FromMinutes(2));
        var check = () => Subject().CheckSessionAsync(TestContext.Current.CancellationToken);

        (await check.Should().ThrowAsync<ReaderException>()).Which.Reason.Should().Be(ConnectionReasons.ExtensionSilent);
    }

    [Fact]
    public async Task should_list_only_the_conversations_that_the_extension_has_read_in_the_order_of_the_list_and_at_most_the_limit()
    {
        var now = _clock.GetUtcNow();
        _inbox.RecordList([new ConversationHeader("a", "A", 0), new ConversationHeader("b", "B", 1), new ConversationHeader("c", "C", 2)], now);
        _inbox.RecordConversation("c", "C", [new Turn("user", null, "x")], now);
        _inbox.RecordConversation("a", "A", [new Turn("user", null, "y")], now);

        (await Subject().ListRecentAsync(10, TestContext.Current.CancellationToken)).Select(header => (header.Id, header.Rank)).Should().Equal(("a", 0), ("c", 2));
        (await Subject().ListRecentAsync(1, TestContext.Current.CancellationToken)).Select(header => header.Id).Should().Equal("a");
    }

    [Fact]
    public async Task should_open_a_conversation_once_and_nothing_for_one_that_was_not_brought()
    {
        _inbox.RecordConversation("a", "A", [new Turn("user", null, "hi")], _clock.GetUtcNow());

        (await Subject().OpenAsync("a", TestContext.Current.CancellationToken)).Should().ContainSingle();
        (await Subject().OpenAsync("a", TestContext.Current.CancellationToken)).Should().BeEmpty();
        (await Subject().OpenAsync("never", TestContext.Current.CancellationToken)).Should().BeEmpty();
    }
}
