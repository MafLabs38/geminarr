using Geminarr.Core.Connection;
using Geminarr.Core.Pipeline;
using Geminarr.Core.Test.Support;

namespace Geminarr.Core.Test.Pipeline;

public sealed class WebReaderFixture
{
    [Fact]
    public async Task should_say_there_is_no_profile_when_no_browser_is_configured_and_not_attach()
    {
        var browser = new FakeBrowser { Configured = false };

        var check = await new WebReader(browser).CheckSessionAsync(TestContext.Current.CancellationToken);

        check.Should().Be(SessionCheck.NoProfile);
        browser.Checks.Should().Be(0);
    }

    [Theory]
    [InlineData(SessionCheck.SignedIn)]
    [InlineData(SessionCheck.SignedOut)]
    public async Task should_ask_the_browser_when_one_is_configured(SessionCheck expected)
    {
        var browser = new FakeBrowser { Check = expected };

        var check = await new WebReader(browser).CheckSessionAsync(TestContext.Current.CancellationToken);

        check.Should().Be(expected);
        browser.Checks.Should().Be(1);
    }

    [Fact]
    public async Task should_list_nothing_yet_and_refuse_to_open_a_conversation_until_the_reader_exists()
    {
        var reader = new WebReader(new FakeBrowser());

        (await reader.ListRecentAsync(10, TestContext.Current.CancellationToken)).Should().BeEmpty();
        reader.Source.Should().Be(ConversationSources.Web);
        var open = () => reader.OpenAsync("c1", TestContext.Current.CancellationToken);
        await open.Should().ThrowAsync<ReaderException>();
    }
}
